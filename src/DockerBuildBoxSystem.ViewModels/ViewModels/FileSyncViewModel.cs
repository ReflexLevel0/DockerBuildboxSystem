using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using DockerBuildBoxSystem.ViewModels.Messages;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public partial class FileSyncViewModel : ViewModelBase,
        IRecipient<SelectedContainerChangedMessage>,
        IRecipient<ContainerRunningMessage>
    {
        private readonly IFileSyncService _fileSyncService;
        private readonly ISettingsService _settingsService;
        private readonly IViewModelLogger _logger;

        //tracks containers that have been force-synced and the path they were synced with
        private readonly Dictionary<string, string> _forceSyncedContainers = new();

        private string ContainerId
        {
            get => SelectedContainer?.Id ?? string.Empty;
        }
        private bool IsContainerRunning => SelectedContainer?.IsRunning == true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartSyncOutCommand))]
        private ContainerInfo? _selectedContainer;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartSyncOutCommand))]
        private bool _isSwitching;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopSyncCommand))]
        private bool _isSyncRunning;



        [ObservableProperty]
        private string _hostSyncPath = string.Empty;

        [ObservableProperty]
        private string _syncOutPath = string.Empty;

        [ObservableProperty]
        private string _containerSyncPath = "/data/";

        [ObservableProperty]
        private string _containerSyncOutPath = "build";

        //synchronization semaphore and cancellation token source for sync operations
        private readonly object _autoSyncSemaphore = new();
        private CancellationTokenSource? _autoSyncCts;
        private readonly AppConfig _appConfig;

        public FileSyncViewModel(AppConfig config, IFileSyncService fileSyncService, ISettingsService settingsService, IViewModelLogger logger)
        {
            // Loading build directory path
            _appConfig = config;
            if(!string.IsNullOrWhiteSpace(config.BuildDirectoryPath))
            {
                ContainerSyncOutPath = config.BuildDirectoryPath;
            }

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
            _settingsService.SyncOutPathChanged += OnSyncOutPathChanged;
            _ = InitializeSettingsAsync();

            //register to receive messages
            WeakReferenceMessenger.Default.RegisterAll(this);
        }


        private void CancelAutoSync()
        {
            CancellationTokenSource? cts;

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

                //check if we need to force sync
                bool shouldForceSync = true;
                if (_forceSyncedContainers.TryGetValue(ContainerId, out string? lastSyncedPath) && lastSyncedPath == HostSyncPath)
                {
                    shouldForceSync = false;
                }

                if (shouldForceSync)
                {
                    //force sync first
                    await StartForceSyncCoreAsync(ct);
                    ct.ThrowIfCancellationRequested();
                }
                else
                {
                    _logger.LogWithNewline($"[sync] Skipping force sync for {ContainerId} (already synced with {HostSyncPath})", false, false);
                }

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

        public void Receive(ContainerRunningMessage message)
        {
            if (SelectedContainer == null || SelectedContainer.Id != message.Value.Id)
                return;

            if (string.IsNullOrEmpty(HostSyncPath) || !Directory.Exists(HostSyncPath))
            {
                _logger.LogWithNewline("[sync] Warning: Host sync path is not set! Can't start auto sync.", true, false);
                return;
            }

            //container is running: start auto sync (force sync if needed)
            StartAutoSync();
        }

        private async Task InitializeSettingsAsync()
        {
            await _settingsService.LoadSettingsAsync();
            HostSyncPath = _settingsService.SourceFolderPath;
            SyncOutPath = _settingsService.SyncOutFolderPath;
        }
        private void OnSourcePathChanged(object? sender, string newPath)
        {
            HostSyncPath = newPath;
            
            //if container is running, restart sync with new path
            if (IsContainerRunning)
            {
                _logger.LogWithNewline($"[sync] Source path changed to {newPath}. Restarting sync...", false, false);
                StartAutoSync();
            }
        }

        private void OnSyncOutPathChanged(object? sender, string newPath)
        {
            SyncOutPath = newPath;
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
                await _fileSyncService.StartWatchingAsync(HostSyncPath, ContainerId, ContainerSyncPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[sync-error] {ex.Message}", true, false);
                SetOnUiThread(() => IsSyncRunning = false);
            }
        }

        private bool CanForceSync() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning;


        /// <summary>
        /// Starts the force sync operation (same constraints as startSync).
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanForceSync))]
        public Task StartForceSyncAsync() => StartForceSyncCoreAsync(CancellationToken.None);

        /// <summary>
        /// Initiates an asynchronous synchronization of files from the container to the host directory.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>

        [RelayCommand(CanExecute = nameof(CanForceSync))]
        public async Task StartSyncOutAsync()
        {
            if (string.IsNullOrWhiteSpace(SyncOutPath))
            {
                _logger.LogWithNewline("[sync-out] Error: Sync out path is not set.", true, false);
                return;
            }

            if (string.IsNullOrWhiteSpace(ContainerSyncPath))
            {
                _logger.LogWithNewline("[sync-out] Error: Container sync path is not configured.", true, false);
                return;
            }

            if (string.IsNullOrWhiteSpace(ContainerSyncOutPath))
            {
                _logger.LogWithNewline("[sync-out] Error: Container sync out path is not configured.", true, false);
                return;
            }

            try
            {
                string buildSyncOutPath = Path.Combine(SyncOutPath, _appConfig.BuildDirectoryPath);
                if (Directory.Exists(buildSyncOutPath))
                {
                    Directory.Delete(buildSyncOutPath, true);
                }
                Directory.CreateDirectory(SyncOutPath);
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[sync-out] Error creating sync out directory: {ex.Message}", true, false);
                throw;
            }

            bool wasWatching = IsSyncRunning;

            try
            {
                //pause the file watcher to prevent events from files being copied
                if (wasWatching)
                {
                    _fileSyncService.PauseWatching();
                }

                _logger.LogWithNewline("[sync-out] Starting sync from container to host...", false, false);

                //construct container path, ensuring proper format
                string containerPath = $"{ContainerSyncPath.TrimEnd('/')}/{ContainerSyncOutPath.TrimStart('/')}";

                _fileSyncService.Configure(SyncOutPath, ContainerId, containerPath);
                await _fileSyncService.ForceSyncFromContainerAsync().ConfigureAwait(false);

                _logger.LogWithNewline("[sync-out] Completed sync from container to host.", false, false);
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[sync-out] Error: {ex.Message}", true, false);
            }
            finally
            {
                //resume the file watcher if it was running before
                if (wasWatching)
                {
                    //reconfigure back to the original host sync path
                    _fileSyncService.Configure(HostSyncPath, ContainerId, ContainerSyncPath);
                    _fileSyncService.ResumeWatching();
                }
            }
        }

        private async Task StartForceSyncCoreAsync(CancellationToken ct)
        {
            // Set IsSyncRunning on UI thread
            SetOnUiThread(() => IsSyncRunning = true);

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

                await _fileSyncService.CleanDirectoryAsync([ContainerSyncOutPath], ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                await _fileSyncService.ForceSyncAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                //update the tracker
                if (!string.IsNullOrEmpty(ContainerId))
                {
                    _forceSyncedContainers[ContainerId] = HostSyncPath;
                }

                _logger.LogWithNewline("[force-sync] Completed force sync operation", false, false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWithNewline("[force-sync] Operation cancelled.", true, false);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[force-sync] Error: {ex.Message}", true, false);
                throw;
            }
            finally
            {
                // Reset IsSyncRunning on UI thread
                SetOnUiThread(() => IsSyncRunning = false);
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
            GC.SuppressFinalize(this);
        }

    }
}
