using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public partial class FileSyncViewModel : ViewModelBase
    {
        private readonly IFileSyncService _fileSyncService;
        private readonly ISettingsService _settingsService;
        private readonly IViewModelLogger _logger;
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
        private bool _isCommandRunning;

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
        private bool CanSync() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning && !IsSyncRunning && !IsCommandRunning && !IsSwitching;

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

        private bool CanForceSync() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == true) && !IsCommandRunning;


        /// <summary>
        /// Starts the force sync operation (same constraints as startSync).
        /// This functiona should delete all sync in data from docker volume and resync everything.
        /// To be implemented once file sync functionality is in place.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanForceSync))]
        public async Task StartForceSyncAsync()
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

                _fileSyncService.Configure(HostSyncPath, ContainerId, ContainerSyncPath);

                await _fileSyncService.CleanDirectoryAsync(["build"]);
                await _fileSyncService.ForceSyncAsync();

                _logger.LogWithNewline("[force-sync] Completed force sync operation", false, false);
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[force-sync-error] {ex.Message}", true, false);
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
        public async Task StopSyncAsync()
        {
            _fileSyncService.StopWatching();
            IsSyncRunning = false;
            await Task.CompletedTask;
        }

        public override async ValueTask DisposeAsync()
        {
            _settingsService.SourcePathChanged -= OnSourcePathChanged;
            _fileSyncService.StopWatching();
            await base.DisposeAsync();
        }

        #region Auto Sync
        /// <summary>
        /// Starts or stops sync automatically when the selected container changes.
        /// </summary>
        /// <param name="oldValue"> the old container value</param>
        /// <param name="newValue"> the new container value</param>
        partial void OnSelectedContainerChanged(ContainerInfo? oldValue, ContainerInfo? newValue)
        {
            if (oldValue?.Id == newValue?.Id)
                return;

            // Stop any existing sync when container changes
            _fileSyncService.StopWatching();
            IsSyncRunning = false;

            // start sync automatically if the new container is running
            if (newValue?.IsRunning == true &&
                !string.IsNullOrWhiteSpace(ContainerId))
            {
                 _ = StartSyncAsync();
            }
        }
        #endregion

    }
}
