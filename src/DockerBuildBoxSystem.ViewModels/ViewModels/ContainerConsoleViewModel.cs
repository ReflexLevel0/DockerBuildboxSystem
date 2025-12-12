using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{

    /// <summary>
    /// ViewModel for a container console that streams logs and executes commands inside Docker containers.
    /// </summary>
    public sealed partial class ContainerConsoleViewModel : ViewModelBase
    {
        private readonly IContainerService _service;
        private readonly IFileSyncService _fileSyncService;
        private readonly IConfiguration _configuration;
        private readonly ISettingsService _settingsService;
        private readonly IClipboardService? _clipboard;
        private readonly ILogRunner _logRunner;
        private readonly ICommandRunner _cmdRunner;
        private readonly IUserControlService _userControlService;
        private readonly IExternalProcessService _externalProcessService;
        private readonly int maxControls = 15;
        private List<UserVariables> _userVariables = new();


        public readonly UILineBuffer UIHandler;

        /// <summary>
        /// Lines currently displayed in the console UI.
        /// </summary>
        public RangeObservableCollection<ConsoleLine> Lines { get; } = new RangeObservableCollection<ConsoleLine>();

        /// <summary>
        /// List of available containers on the host.
        /// </summary>
        public ObservableCollection<ContainerInfo> Containers { get; } = new();

        /// <summary>
        /// Defined user variables for command resolution.
        /// </summary>
        public ObservableCollection<IUserControlViewModel> UserControls { get; } = new();

        /// <summary>
        /// The raw user input text bound from the UI.
        /// </summary>
        [ObservableProperty]
        private string? _input;

        private SynchronizationContext? _synchronizationContext;

        /// <summary>
        /// Currently selected container id OR name.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestartContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunUserCommandCommand))]
        [NotifyPropertyChangedFor(nameof(CanUseUserControls))]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]       
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
        private string _containerId = string.Empty;

        public bool CanUseUserControls => CanSend();

        public bool IsRunning => SelectedContainer?.IsRunning == true;

        /// <summary>
        /// True while logs are currently being streamed.
        /// </summary>
        public bool IsLogsRunning => _logRunner.IsRunning;

        /// <summary>
        /// True while a command is being executed.
        /// </summary>
        public bool IsCommandRunning => _cmdRunner.IsRunning;

        /// <summary>
        /// The selected container info object.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestartContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunUserCommandCommand))]
        [NotifyPropertyChangedFor(nameof(CanUseUserControls))]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(OpenContainerInCmdCommand))]
        private ContainerInfo? _selectedContainer;

        /// <summary>
        /// True while sync is being executed.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunUserCommandCommand))]
        [NotifyPropertyChangedFor(nameof(CanUseUserControls))]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopSyncCommand))]
        public bool _isSyncRunning;

        /// <summary>
        /// If true, include stopped containers in the list.
        /// </summary>
        [ObservableProperty]
        private bool _showAllContainers = true;

        /// <summary>
        /// True while the containers list is being refreshed.
        /// </summary>
        [ObservableProperty]
        private bool _isLoadingContainers;

        /// <summary>
        /// If true, automatically start streaming logs when a container becomes selected.
        /// </summary>
        [ObservableProperty]
        private bool _autoStartLogs = true;

        private int _switchingCount;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunUserCommandCommand))]
        [NotifyPropertyChangedFor(nameof(CanUseUserControls))]
        private bool _isSwitching;
        /// <summary>
        /// The host path to sync files from.
        /// </summary>
        [ObservableProperty]
        private string _hostSyncPath = string.Empty;

        /// <summary>
        /// The container path to sync files to.
        /// </summary>
        [ObservableProperty]
        private string _containerSyncPath = "/data/";

        // Track previous selected container id to manage stop-on-switch behavior
        private string? _previousContainerId;

        private readonly SemaphoreSlim _containerSwitchLock = new(1, 1);
        private CancellationTokenSource? _switchCts;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerConsoleViewModel"/> class.
        /// </summary>
        /// <param name="service">The container service used to interact with containers, ex Docker.</param>
        /// <param name="fileSyncService">The file sync service.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="settingsService">The settings service.</param>
        /// <param name="clipboard">Optional clipboard service for copying the text output.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="service"/> is null.</exception>
        public ContainerConsoleViewModel(
            IContainerService service, 
            IFileSyncService fileSyncService,
            IConfiguration configuration,
            ISettingsService settingsService,
            IUserControlService userControlService,
            ILogRunner logRunner,
            ICommandRunner cmdRunner,
            IExternalProcessService externalProcessService,
            IClipboardService? clipboard = null) : base()
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _fileSyncService = fileSyncService ?? throw new ArgumentNullException(nameof(fileSyncService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _userControlService = userControlService ?? throw new ArgumentNullException(nameof(userControlService));
            _externalProcessService = externalProcessService ?? throw new ArgumentNullException(nameof(externalProcessService));
            _logRunner = logRunner ?? throw new ArgumentNullException(nameof(logRunner));
            _cmdRunner = cmdRunner ?? throw new ArgumentNullException(nameof(cmdRunner));
            _clipboard = clipboard;


            //initialize from settings service
            _ = InitializeSettingsAsync();

            _cmdRunner.RunningChanged += (_, __) =>
                SetOnUiThread(() =>
                {
                    OnPropertyChanged(nameof(IsCommandRunning));
                    SendCommand.NotifyCanExecuteChanged();
                    RunUserCommandCommand.NotifyCanExecuteChanged();
                    StopExecCommand.NotifyCanExecuteChanged();
                    InterruptExecCommand.NotifyCanExecuteChanged();
                    StartSyncCommand.NotifyCanExecuteChanged();
                    StartForceSyncCommand.NotifyCanExecuteChanged();
                });

            _logRunner.RunningChanged += (_, __) =>
                SetOnUiThread(() =>
                {
                    OnPropertyChanged(nameof(IsLogsRunning));
                    StartLogsCommand.NotifyCanExecuteChanged();
                    StopLogsCommand.NotifyCanExecuteChanged();
                });

            _fileSyncService.Changes.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (string item in e.NewItems)
                    {
                        PostLogMessage($"[sync] {item}", false);
                    }
                }
            };
            
            //listen for settings changes
            _settingsService.SourcePathChanged += OnSourcePathChanged;

            PropertyChanged += async (s, e) =>
            {
                if(string.Compare(e.PropertyName, nameof(SelectedContainer)) == 0) {
                    await OnSelectedContainerChangedAsync(SelectedContainer);
                }
            };

            UIHandler = new UILineBuffer(Lines);

            // Periodically refreshing container info 
            var refreshContainersTimer = new System.Timers.Timer(new TimeSpan(0, 0, 5));
            refreshContainersTimer.Elapsed += async (_, _) =>
            {
                if (_synchronizationContext != null)
                {
                    _synchronizationContext.Post(async _ => await RefreshContainersAsync(), null);
                }
            };
            refreshContainersTimer.Enabled = true;
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
                UIHandler.EnqueueLine($"[sync] Warning: Source path changed to {newPath}. Stop and restart sync to apply.", true);
            }
        }

        /// <summary>
        /// Initializes the ViewModel: 
        ///     starts the UI update loop, 
        ///     refreshes containers, 
        ///     loads user controls, 
        ///     and optionally starts logs.
        /// </summary>
        [RelayCommand]
        private async Task InitializeAsync()
        {
            // Start the global UI update task
            UIHandler.Start();

            // Load available containers on initialization
            await RefreshContainersCommand.ExecuteAsync(null);

            // Load user-defined controls
            await LoadUserControlsAsync();

            // Optionally auto-start logs if ContainerId is set
            if (AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
            {
                await StartLogsCommand.ExecuteAsync(null);
            }
        }

        #region Console Management

        /// <summary>
        /// Clears all lines from the console.
        /// </summary>
        [RelayCommand]
        private async Task ClearAsync()
        {
            UIHandler.ClearAsync();
        }

        /// <summary>
        /// The copy command to copy output of the container to clipboard.
        /// </summary>
        [RelayCommand]
        private async Task CopyAsync()
        {
            if(_clipboard is null)
                return;

            await UIHandler.CopyAsync(_clipboard);
        }

        #endregion
        private void PostLogMessage(string message, bool isError, bool isImportant = false) => UIHandler.EnqueueLine(message + "\r\n", isError, isImportant);

        #region Cleanup

        /// <summary>
        /// cancel and cleanup task
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            try
            {
                _switchCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                //The token was already disposed by a concurrent switch operation
            }
            finally
            {
                _switchCts?.Dispose();
                _switchCts = null;
            }

            _containerSwitchLock?.Dispose();

            _settingsService.SourcePathChanged -= OnSourcePathChanged;
            _fileSyncService.StopWatching();
            await StopLogsAsync();
            await StopExecAsync();
            await UIHandler.StopAsync();
            await base.DisposeAsync();
        }

        #endregion

        public void SetSynchronizationContext(SynchronizationContext? context)
        {
            _synchronizationContext = context;
        }
    }
}
