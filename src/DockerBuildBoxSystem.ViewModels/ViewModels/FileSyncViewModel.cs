using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using DockerBuildBoxSystem.ViewModels.Messages;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public partial class FileSyncViewModel : ViewModelBase,
        IRecipient<SelectedContainerChangedMessage>,
        IRecipient<ContainerStartedMessage>,
        IRecipient<ContainerRunningMessage>
    {
        private readonly IFileSyncService _fileSyncService;
        private readonly ISettingsService _settingsService;
        private readonly IViewModelLogger _logger;

        //tracks if we just handled a ContainerStartedMessage to avoid duplicate sync start
        private string? _lastStartedContainerId;

        private string ContainerId
        {
            get => SelectedContainer?.Id ?? string.Empty;
        }
        private bool IsContainerRunning => SelectedContainer?.IsRunning == true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
        private ContainerInfo? _selectedContainer;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
        private bool _isSwitching;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopSyncCommand))]
        private bool _isSyncRunning;



        [ObservableProperty]
        private string _hostSyncPath = string.Empty;

        [ObservableProperty]
        private string _containerSyncPath = "/data/";

        //synchronization semaphore and cancellation token source for sync operations
        private readonly object _autoSyncSemaphore = new();
        private CancellationTokenSource? _autoSyncCts;

        public FileSyncViewModel(IFileSyncService fileSyncService, ISettingsService settingsService, IViewModelLogger logger)
        {
            _fileSyncService = fileSyncService ?? throw new ArgumentNullException(nameof(fileSyncService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _fileSyncService.Changes.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (string item in e.NewItems)
                    {
                        _logger.LogWithNewline($"[sync] {item}", false, false);
                    }
                }
            };

            _settingsService.SourcePathChanged += OnSourcePathChanged;
            InitializeSettingsAsync();

            //register to receive messages
            WeakReferenceMessenger.Default.RegisterAll(this);
        }
        private void CancelAutoSync()
        {
            CancellationTokenSource? cts = null;

            lock (_autoSyncSemaphore)
            {
                cts = _autoSyncCts;
                _autoSyncCts = null;
            }

            if (cts is null) return;

            try { cts.Cancel(); } catch { /* ignore */ }
            cts.Dispose();
        }

        private void StartAutoSync()
        {
            //start by canceling any existing auto-sync operation
            CancelAutoSync();

            var cts = new CancellationTokenSource();
            lock (_autoSyncSemaphore)
            {
                _autoSyncCts = cts;
            }

            _ = RunAutoSyncAsync(cts.Token);
        }

        private async Task RunAutoSyncAsync(CancellationToken ct)
        {
            try
            {
                await StopSyncCoreAsync();

                //force sync first
                await StartForceSyncCoreAsync(ct);
                ct.ThrowIfCancellationRequested();

                //then start normal sync/watching
                await StartSyncAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWithNewline("[sync] Auto sync start cancelled.", true, false);
                await StopSyncCoreAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[sync-error] Auto sync start failed: {ex.Message}", true, false);
                await StopSyncCoreAsync();
            }
        }

        partial void OnIsSyncRunningChanged(bool value)
        {
            //notify other view models about sync running state change
            WeakReferenceMessenger.Default.Send(new IsSyncRunningChangedMessage(value));
        }

        /// <summary>
        /// Handles the SelectedContainerChangedMessage.
        /// </summary>
        public void Receive(SelectedContainerChangedMessage message)
        {
            SelectedContainer = message.Value;
        }

        public void Receive(ContainerStartedMessage message)
        {
            if (SelectedContainer == null || SelectedContainer.Id != message.Value.Id)
                return;

            if (string.IsNullOrEmpty(HostSyncPath) || !Directory.Exists(HostSyncPath))
            {
                _logger.LogWithNewline("[sync] Warning: Host sync path is not set! Can't run force sync on container start.", true, false);
                return;
            }

            //track that we handled this container start to avoid duplicate sync in ContainerRunningMessage
            _lastStartedContainerId = message.Value.Id;

            //container just started: run force sync then start auto sync
            StartAutoSync();
        }

        public void Receive(ContainerRunningMessage message)
        {
            if (SelectedContainer == null || SelectedContainer.Id != message.Value.Id)
                return;

            //if we just handled ContainerStartedMessage for this container, skip (already started sync)
            if (_lastStartedContainerId == message.Value.Id)
            {
                _lastStartedContainerId = null;
                return;
            }

            if (string.IsNullOrEmpty(HostSyncPath) || !Directory.Exists(HostSyncPath))
            {
                _logger.LogWithNewline("[sync] Warning: Host sync path is not set! Can't start auto sync.", true, false);
                return;
            }

            //container was already running: only start auto sync (no force sync)
            _ = StartSyncAsync();
        }

        private async Task InitializeSettingsAsync()
        {
            await _settingsService.LoadSettingsAsync();
            HostSyncPath = _settingsService.SourceFolderPath;
        }
        private void OnSourcePathChanged(object? sender, string newPath)
        {
            HostSyncPath = newPath;
            //if sync is running, we might want to restart it or notify user
            if (IsSyncRunning)
            {
                _logger.LogWithNewline($"[sync] Warning: Source path changed to {newPath}. Stop and restart sync to apply.", true, false);
            }
        }

        /// <summary>
        /// Determines whether sync can be started.
        /// </summary>
        private bool CanSync() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning && !IsSyncRunning && !IsSwitching;

        /// <summary>
        /// Starts the sync operation.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSync))]
        public async Task StartSyncAsync()
        {
            if (string.IsNullOrWhiteSpace(HostSyncPath))
            {
                _logger.LogWithNewline("[sync-error] Error: Host sync path is not set!", true, false);
                return;
            }

            if (!Directory.Exists(HostSyncPath))
            {
                _logger.LogWithNewline($"[sync-error] Error: Host directory does not exist: {HostSyncPath}", true, false);
                return;
            }

            IsSyncRunning = true;
            try
            {
                _fileSyncService.StartWatching(HostSyncPath, ContainerId, ContainerSyncPath);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[sync-error] {ex.Message}", true, false);
                IsSyncRunning = false;
            }
        }

        private bool CanForceSync() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning;


        /// <summary>
        /// Starts the force sync operation (same constraints as startSync).
        /// This functiona should delete all sync in data from docker volume and resync everything.
        /// To be implemented once file sync functionality is in place.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanForceSync))]
        public Task StartForceSyncAsync() => StartForceSyncCoreAsync(CancellationToken.None);

        private async Task StartForceSyncCoreAsync(CancellationToken ct)
        {
            IsSyncRunning = true;

            try
            {
                _logger.LogWithNewline("[force-sync] Starting force sync operation", false, false);

                if (string.IsNullOrWhiteSpace(HostSyncPath) || !Directory.Exists(HostSyncPath))
                {
                    _logger.LogWithNewline("[force-sync] Error: Host sync path is invalid.", true, false);
                    return;
                }

                ct.ThrowIfCancellationRequested();

                _fileSyncService.Configure(HostSyncPath, ContainerId, ContainerSyncPath);

                await _fileSyncService.CleanDirectoryAsync(["build"], ct);
                ct.ThrowIfCancellationRequested();

                await _fileSyncService.ForceSyncAsync(ct);
                ct.ThrowIfCancellationRequested();

                _logger.LogWithNewline("[force-sync] Completed force sync operation", false, false);
            }
            finally
            {
                IsSyncRunning = false;
            }
        }

        /// <summary>
        /// Determines whether sync operation can be stopped.
        /// </summary>
        private bool CanStopSync() => IsSyncRunning;

        /// <summary>
        /// Stops the current sync task, if it is running.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopSync))]
        private async Task StopSyncAsync()
        {
            CancelAutoSync();
            await StopSyncCoreAsync();
        }
        public Task StopSyncCoreAsync()
        {
            _fileSyncService.StopWatching();
            IsSyncRunning = false;
            return Task.CompletedTask;
        }


        public override async ValueTask DisposeAsync()
        {
            CancelAutoSync();
            WeakReferenceMessenger.Default.UnregisterAll(this);
            _settingsService.SourcePathChanged -= OnSourcePathChanged;
            _fileSyncService.StopWatching();
            await base.DisposeAsync();
        }

    }
}
