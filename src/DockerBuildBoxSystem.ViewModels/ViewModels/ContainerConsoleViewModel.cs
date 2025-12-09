using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private readonly IImageService _imageService;
        private readonly IFileSyncService _fileSyncService;
        private readonly IConfiguration _configuration;
        private readonly ISettingsService _settingsService;
        private readonly IClipboardService? _clipboard;
        private readonly ILogRunner _logRunner;
        private readonly ICommandRunner _cmdRunner;
        private readonly IUserControlService _userControlService;
        private readonly int maxControls = 15;
        private List<UserVariables> _userVariables = new();


        public readonly UILineBuffer UIHandler;

        /// <summary>
        /// Lines currently displayed in the console UI.
        /// </summary>
        public RangeObservableCollection<ConsoleLine> Lines { get; } = new RangeObservableCollection<ConsoleLine>();

        /// <summary>
        /// List of available images on the host.
        /// </summary>
        public ObservableCollection<ImageInfo> Images { get; } = new();

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
        public string ContainerId
        {
            get => SelectedContainer?.Id ?? string.Empty;
        }

        /// <summary>
        /// Currently selected image id OR name.
        /// </summary>
        public string ImageId
        {
            get => SelectedImage?.Id ?? string.Empty;
        }


        public bool CanUseUserControls => CanSend();

        public bool IsContainerRunning => SelectedContainer?.IsRunning == true;

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
        private ContainerInfo? _selectedContainer;

        /// <summary>
        /// The selected image info object.
        /// </summary>
        [ObservableProperty]
        private ImageInfo? _selectedImage;

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
        /// If true, include intermediate containers in the list.
        /// </summary>
        [ObservableProperty]
        private bool _showAllImages = true;

        /// <summary>
        /// True while the image list is being refreshed.
        /// </summary>
        [ObservableProperty]
        private bool _isLoadingImages;

        /// <summary>
        /// If true, automatically start streaming logs when a container becomes selected.
        /// </summary>
        [ObservableProperty]
        private bool _autoStartLogs = true;

        private int _switchingCount;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunUserCommandCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartSyncCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartForceSyncCommand))]
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

        //track previous selected container and image id to manage stop-on-switch behavior
        private string? _previousContainerId;
        private string? _previousImageId;

        private readonly SemaphoreSlim _imageSwitchLock = new(1, 1);
        private CancellationTokenSource? _switchCts;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerConsoleViewModel"/> class.
        /// </summary>
        /// <param name="service">The container service used to interact with containers, ex Docker.</param>
        /// <param name="imageService">The image service used to interact with images.</param>
        /// <param name="fileSyncService">The file sync service.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="settingsService">The settings service.</param>
        /// <param name="clipboard">Optional clipboard service for copying the text output.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="service"/> is null.</exception>
        public ContainerConsoleViewModel(
            IContainerService service, 
            IImageService imageService,
            IFileSyncService fileSyncService,
            IConfiguration configuration,
            ISettingsService settingsService,
            IUserControlService userControlService,
            ILogRunner logRunner,
            ICommandRunner cmdRunner,
            IClipboardService? clipboard = null) : base()
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
            _fileSyncService = fileSyncService ?? throw new ArgumentNullException(nameof(fileSyncService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _userControlService = userControlService ?? throw new ArgumentNullException(nameof(userControlService));
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
                if(string.Compare(e.PropertyName, nameof(SelectedImage)) == 0) {
                    await OnSelectedImageChangedAsync(SelectedImage);
                }
                if(string.Compare(e.PropertyName, nameof(SelectedContainer)) == 0) {
                    await OnSelectedContainerChangedAsync(SelectedContainer);
                }
            };

            UIHandler = new UILineBuffer(Lines);

            // Periodically refreshing container and image info 
            var refreshImagesContainersTimer = new System.Timers.Timer(new TimeSpan(0, 0, 5));
            refreshImagesContainersTimer.Elapsed += async (_, _) =>
            {
                if (_synchronizationContext != null)
                {
                    _synchronizationContext.Post(async _ =>
                    {
                        await RefreshSelectedContainerAsync();
                        await RefreshImagesAsync();
                    }, null);
                }
            };
            refreshImagesContainersTimer.Enabled = true;
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

            // Load available images on initialization
            await RefreshImagesCommand.ExecuteAsync(null);

            // Load user-defined controls
            await LoadUserControlsAsync();

            // Optionally auto-start logs if ContainerId is set
            if (AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
            {
                await StartLogsCommand.ExecuteAsync(null);
            }
        }


        #region Container Management
        /// <summary>
        /// Refreshes the list of images from the image service.
        /// </summary>
        [RelayCommand]
        private async Task RefreshImagesAsync()
        {
            var selectedImageId = SelectedImage?.Id;
            IsLoadingImages = true;
            try
            {
                //not using ConfigureAwait(false) since we want to return to the UI thread as soon as possible (no stalling :))
                var images = await _imageService.ListImagesAsync(all: ShowAllImages);

                //Back to the UI threa so safe to update ObservableCollection
                Images.Clear();
                foreach (var image in images)
                {
                    if(string.Compare(image.Id, selectedImageId) == 0)
                    {
                        SelectedImage = image;
                    }
                    Images.Add(image);
                }
            }
            catch (Exception ex)
            {
                PostLogMessage($"[image-list-error] {ex.Message}", true);
            }
            finally
            {
                IsLoadingImages = false;
            }
        }

        /// <summary>
        /// Updates dependent state when the selected image changes.
        /// </summary>
        /// <param name="value">The newly selected image info or null.</param>
        public async Task OnSelectedImageChangedAsync(ImageInfo? value)
        {
            //if images are still loading and selection is reset, ignore.
            if (IsLoadingImages && value is null)
                return;

            var newImageId = value?.Id;
            if (newImageId == _previousImageId)
                return;

            _previousImageId = newImageId;

            Interlocked.Increment(ref _switchingCount);
            IsSwitching = true;

            //cancel any pending start operations from a previous selection
            try
            {
                _switchCts?.Cancel();
            }
            catch (ObjectDisposedException) { /* ignoring... */ }

            _switchCts?.Dispose();
            _switchCts = new CancellationTokenSource();
            var ct = _switchCts.Token;

            var takeLock = false;
            try
            {
                //make switch operation cancelable  to avoid releasing an unacquired lock.
                await _imageSwitchLock.WaitAsync(ct);
                takeLock = true;

                ct.ThrowIfCancellationRequested();

                var newImage = value;

                //fallback to current if previous not tracked yet
                var oldContainerId = string.IsNullOrWhiteSpace(_previousContainerId) ? ContainerId : _previousContainerId;

                //stop any running operations from previous container.
                await StopLogsAsync();
                await StopExecAsync();
                await StopSyncAsync();
                UIHandler.DiscardPending();

                if (!string.IsNullOrWhiteSpace(oldContainerId))
                {
                    try
                    {
                        var oldContainer = await _service.InspectAsync(oldContainerId, ct);
                        if (oldContainer.IsRunning)
                        {
                            await StopContainerByIdAsync(oldContainer);
                        }
                    }
                    catch
                    {
                        /* ignoring... */
                    }
                }

                ct.ThrowIfCancellationRequested();

                if(newImage is null)
                {
                    SelectedImage = null;
                    _previousContainerId = ContainerId;
                    return;
                }

                var primaryTag = newImage.RepoTags.FirstOrDefault();
                var imageName = primaryTag ?? newImage.Id;

                PostLogMessage($"[info] Selected image: {imageName}", false);

                //try to find an existing container for this image.
                var containers = await _service.ListContainersAsync(all: true, ct: ct);

                var existingContainer = containers.FirstOrDefault(c =>
                    (!string.IsNullOrEmpty(primaryTag) && c.Image == primaryTag) || c.Image == newImage.Id);

                ContainerInfo container;
                if (existingContainer is not null)
                {
                    PostLogMessage(
                        $"[info] Found existing container: {existingContainer.Names.FirstOrDefault() ?? existingContainer.Id}",
                        false);

                    //ensure we get the latest state
                    container = await _service.InspectAsync(existingContainer.Id, ct);
                }
                else
                {
                    PostLogMessage("[info] No existing container found for image. Creating a new one...", false);

                    var newContainerId = await _service.CreateContainerAsync(new ContainerCreationOptions { ImageName = imageName }, ct: ct);
                    container = await _service.InspectAsync(newContainerId, ct);

                    var createdName = container.Names.FirstOrDefault() ?? container.Id;
                    PostLogMessage($"[info] Created new container: {createdName}", false);
                }

                SelectedContainer = container;

                ct.ThrowIfCancellationRequested();

                //start the container
                if (!container.IsRunning)
                {
                    await StartContainerInternalAsync(ct);
                }

                //auto-start logs
                if (AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
                {
                    _ = StartLogs();
                }

                _previousContainerId = ContainerId;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                PostLogMessage($"[selection-error] {ex.Message}", true);
            }
            finally
            {
                if (takeLock)
                    _imageSwitchLock.Release();

                if (Interlocked.Decrement(ref _switchingCount) == 0)
                    IsSwitching = false;
            }
        }

        private async Task OnSelectedContainerChangedAsync(ContainerInfo? container)
        {
            // If we are switching images, the image switcher handles log starting.
            if (IsSwitching) return;

            if (AutoStartLogs && !IsLogsRunning && container?.IsRunning == true)
            {
                await StartLogsCommand.ExecuteAsync(null);
            }
        }

        /// <summary>
        /// Invoked when the value of the "Show All Images" setting changes.
        /// </summary>
        /// <param name="value">The new value of the "Show All Images" setting.
        /// <see langword="true"/> if all images should be shown; otherwise, <see langword="false"/>.</param>
        partial void OnShowAllImagesChanged(bool value) => RefreshImagesCommand.ExecuteAsync(null);
        private bool CanStartContainer() => !string.IsNullOrWhiteSpace(ContainerId) && !IsContainerRunning;

        /// <summary>
        /// Starts the selected container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartContainer))]
        private async Task StartContainerAsync()
        {
            await _imageSwitchLock.WaitAsync();
            try
            {
                await StartContainerInternalAsync(CancellationToken.None);
            }
            finally
            {
                _imageSwitchLock.Release();
            }
        }

        private async Task StartContainerInternalAsync(CancellationToken ct)
        {
            if (SelectedContainer is null) return;

            if (ct.IsCancellationRequested) return;

            var name = SelectedContainer.Names.FirstOrDefault() ?? SelectedContainer.Id;
            try
            {
                PostLogMessage($"[info] Starting container: {name}", false);

                var status = await _service.StartAsync(ContainerId, ct);

                if (ct.IsCancellationRequested)
                {
                    PostLogMessage($"[info] Startup cancelled for: {name}", false);
                    return;
                }

                if (status)
                {
                    PostLogMessage($"[info] Started container: {name}", false);
                }
                else
                {
                    PostLogMessage($"[start-container] Container did not start: {name}", true);
                }
            }
            catch (OperationCanceledException)
            {
                PostLogMessage($"[info] Operation cancelled: {name}", false);
            }
            catch (Exception ex)
            {
                PostLogMessage($"[start-container-error] {ex.Message}", true);
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    await RefreshSelectedContainerAsync();
                }
            }
        }
        private bool CanStopContainer() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning;

        /// <summary>
        /// Stops a running container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopContainer))]
        private async Task StopContainerAsync() => await StopContainerByIdAsync(SelectedContainer);

        private bool CanRestartContainer() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning;

        /// <summary>
        /// Stops a container by id (used when auto-stopping previous selection).
        /// </summary>
        private async Task StopContainerByIdAsync(ContainerInfo? container)
        {
            if (container is null) return;

            try
            {
                var name = container.Names.FirstOrDefault() ?? container.Id;
                PostLogMessage($"[info] Stopping container: {name}", false);
                await _service.StopAsync(container.Id, timeout: TimeSpan.FromSeconds(10));
                PostLogMessage($"[info] Stopped container: {name}", false);
            }
            catch (Exception ex)
            {
                PostLogMessage($"[stop-container-error] {ex.Message}", true);
            }
            finally
            {
                 await RefreshSelectedContainerAsync();
            }
        }

        /// <summary>
        /// Restarts a running container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRestartContainer))]
        private async Task RestartContainerAsync()
        {
            if (string.IsNullOrWhiteSpace(ContainerId)) return;
            try
            {
                PostLogMessage($"[info] Restarting container: {ContainerId}", false);
                await _service.RestartAsync(ContainerId, timeout: TimeSpan.FromSeconds(10));
                PostLogMessage($"[info] Restarted container: {ContainerId}", false);
            }
            catch (Exception ex)
            {
                PostLogMessage($"[restart-container-error] {ex.Message}", true);
            }
            finally
            {
                await RefreshSelectedContainerAsync();
            }
        }

        #endregion

        #region Command Execution

        /// <summary>
        /// Determines whether sending commands is currently allowed.
        /// </summary>
        private bool CanSend() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning && !IsSyncRunning && !IsSwitching;


        /// <summary>
        /// Executes a user-defined command associated with the specified control asynchronously.
        /// </summary>
        /// <remarks>If the <paramref name="control"/> is <see langword="null"/> or no container is
        /// selected, the method logs a message indicating that no command or container is available. If the control is
        /// a <see cref="ButtonCommand"/> and no command is defined for the button, a corresponding message is logged.
        /// Unsupported control types are also logged.</remarks>
        /// <param name="control">The <see cref="UserControlDefinition"/> representing the control that defines the command to execute. This
        /// parameter can be <see langword="null"/>.</param>
        /// <returns></returns>
        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task RunUserCommandAsync(UserControlDefinition? control)
        {
            // Check for null control or no selected container
            if (control is null || SelectedContainer is null)
            {
                PostLogMessage("[user-cmd] No command or container selected.", true);
                return;
            }
            // Handle ButtonCommand control type
            if (control is ButtonCommand buttonCmd)
            {
                var cmds = buttonCmd.Command;
                if (cmds.Length == 0)
                {
                    PostLogMessage("[user-cmd] No command defined for this button.", true);
                    return;
                }
                var cmdStr = string.Join(' ', cmds);
                await RouteInputAsync(cmdStr);
            }
            else
            {
                PostLogMessage("[user-cmd] Unsupported control type for command execution.", true);
            }
        }

        /// <summary>
        /// Sends the current input text as a command to the container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSend), AllowConcurrentExecutions = true)]
        private async Task SendAsync()
        {
            var raw = (Input ?? string.Empty);
            Input = string.Empty;

            await RouteInputAsync(raw);
        }

        /// <summary>
        /// Executes a command string by splitting it as a argv array.
        /// </summary>
        private async Task ExecuteAndLogAsync(string cmd) => await ExecuteAndLog(ShellSplitter.SplitShellLike(cmd));

        /// <summary>
        /// Executes a command inside the selected container and streams output to the console UI.
        /// </summary>
        /// <param name="args">The command and its arguments (argv form).</param>
        private Task ExecuteAndLog(string[] args)
        {
            //add command to console on UI thread
            PostLogMessage($"> {string.Join(' ', args)}", false);

            return Task.Run(async () =>
            {
                try
                {
                    await foreach (var (isErr, line) in _cmdRunner.RunAsync(_service, ContainerId, args).ConfigureAwait(false))
                    {
                        UIHandler.EnqueueLine(line, isErr);
                    }

                    var exitCode = await _cmdRunner.ExitCode.ConfigureAwait(false);
                    PostLogMessage($"[exit] {exitCode}", false, true);
                }
                catch (OperationCanceledException)
                {
                    PostLogMessage("[exec] canceled", false, true);
                }
                catch (Exception ex)
                {
                    PostLogMessage($"[exec-error] {ex.Message}", true, true);
                }
            });
        }

        /// <summary>
        /// Determines whether command execution can be stopped.
        /// </summary>
        private bool CanStopExec() => _cmdRunner.IsRunning;

        /// <summary>
        /// Stops the current command execution task, if it is running.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopExec))]
        private async Task StopExecAsync()
        {
            await _cmdRunner.StopAsync();
        }

        /// <summary>
        /// Determines whether command execution can be interrupted...
        /// </summary>
        private bool CanInterruptExec() => _cmdRunner.IsRunning;

        /// <summary>
        /// Interrupts the current command execution task, if it is running!
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanInterruptExec))]
        private async Task InterruptExecAsync()
        {
            await _cmdRunner.InterruptAsync();
        }

        #endregion

        #region Log Streaming

        /// <summary>
        /// Determines whether log streaming can be started.
        /// </summary>
        private bool CanStartLogs() => !IsLogsRunning && !string.IsNullOrWhiteSpace(ContainerId);

        /// <summary>
        /// Starts streaming container logs, following container TTY settings.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartLogs))]
        private Task StartLogs()
        {
            return Task.Run(async () =>
            {
                try
                {
                    await foreach (var (isErr, line) in _logRunner.RunAsync(_service, ContainerId).ConfigureAwait(false))
                    {
                        PostLogMessage(line, isErr);
                    }
                }
                catch (OperationCanceledException)
                {
                    PostLogMessage("[logs] canceled", false);
                }
                catch (Exception ex)
                {
                    PostLogMessage($"[logs-error] {ex.Message}", true, true);
                }

            });
        }

        /// <summary>
        /// Determines whether log streaming can be stopped.
        /// </summary>
        private bool CanStopLogs() => IsLogsRunning;

        /// <summary>
        /// Stops the current log streaming task, if it is running.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopLogs))]
        private async Task StopLogsAsync()
        {
            await _logRunner.StopAsync();
        }

        #endregion

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

        #region Sync
        /// <summary>
        /// Determines whether sync can be started.
        /// </summary>
        private bool CanSync() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning && !IsSyncRunning && !IsCommandRunning && !IsSwitching;

        /// <summary>
        /// Starts the sync operation.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSync))]
        private async Task StartSyncAsync()
        {
            if (string.IsNullOrWhiteSpace(HostSyncPath))
            {
                PostLogMessage("[sync-error] Error: Host sync path is not set!", true);
                return;
            }

            if (!Directory.Exists(HostSyncPath))
            {
                PostLogMessage($"[sync-error] Error: Host directory does not exist: {HostSyncPath}", true);
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
                PostLogMessage($"[sync-error] {ex.Message}", true);
                IsSyncRunning = false;
            }
        }

        /// <summary>
        /// Starts the force sync operation (same constraints as startSync).
        /// This functiona should delete all sync in data from docker volume and resync everything.
        /// To be implemented once file sync functionality is in place.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSync))]
        private async Task StartForceSyncAsync()
        {
            IsSyncRunning = true;
            try
            {
                PostLogMessage("[force-sync] Starting force sync operation", false);
                
                if (string.IsNullOrWhiteSpace(HostSyncPath) || !Directory.Exists(HostSyncPath))
                {
                     PostLogMessage("[force-sync] Error: Host sync path is invalid.", true);
                     return;
                }
                
                _fileSyncService.Configure(HostSyncPath, ContainerId, ContainerSyncPath);
                
                await _fileSyncService.ForceSyncAsync();
                
                PostLogMessage("[force-sync] Completed force sync operation", false);
            }
            catch (Exception ex)
            {
                PostLogMessage($"[force-sync-error] {ex.Message}", true);
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
            _fileSyncService.StopWatching();
            IsSyncRunning = false;
            PostLogMessage("[sync] Stopped watching.", false);
            await Task.CompletedTask;
        }
        #endregion

        #region Helpers
        private async Task RefreshSelectedContainerAsync()
        {
            if (string.IsNullOrWhiteSpace(ContainerId)) return;
            try
            {
                SelectedContainer = await _service.InspectAsync(ContainerId);
            }
            catch (Exception ex)
            {
                PostLogMessage($"[refresh-error] {ex.Message}", true);
            }
        }

        private async Task RouteInputAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            // resolve user variables from input
            var resolvedCommand = await _userControlService.RetrieveVariableAsync(raw, _userVariables);

            if (await _cmdRunner.TryWriteToInteractiveAsync(resolvedCommand))
                return;

            var args = ShellSplitter.SplitShellLike(resolvedCommand);
            await ExecuteAndLog(args);
        }

        private void PostLogMessage(string message, bool isError, bool isImportant = false) => UIHandler.EnqueueLine(message + "\r\n", isError, isImportant);
        #endregion

        #region User Controls
        /// <summary>
        /// Load the user-defined controls from the service and populate the ViewModel collection.
        /// then loads any saved user variable values.
        /// </summary>
        /// <remarks>If the number of loaded controls exceeds the maximum allowed,
        /// only the first <paramref name="maxControls"/> will be used.</remarks>
        /// <returns> a task representing the asynchronous operation</returns>
        private async Task LoadUserControlsAsync()
        {
            var controls = await _userControlService.LoadUserControlsAsync() 
                           ?? new List<UserControlDefinition>();

            if (controls.Count > maxControls)
            {
                PostLogMessage($"[user-control] Warning: Loaded controls exceed maximum of {maxControls}. Only the first {maxControls} will be used.", true);
                controls = controls.Take(maxControls).ToList();
            }

            UserControls.Clear();
            foreach (var control in controls)
            {
                AddControlToViewModel(control);
            }
            // After loading controls, load user variables
            LoadUserVariables();
        }


        /// <summary>
        /// Adds a user control definition to the ViewModel collection by creating the appropriate ViewModel instance.
        /// </summary>
        /// <remarks> Uses updateVarAction to update user variable values when controls change.
        /// this ensures that the shared _userVariables list stays in sync with the UI.</remarks>
        /// <param name="control"> the user control definition</param>
        private void AddControlToViewModel(UserControlDefinition control)
        {
            Action<string, string>? updateVarAction = (id, value) =>
            {
                var existingVar = _userVariables.FirstOrDefault(v => v.Id == id);
                if (existingVar != null)
                    existingVar.Value = value;
                
                else
                  _userVariables.Add(new UserVariables (id,value));
                
            };

            // Create appropriate ViewModel based on control type
            switch (control)
            {
                case TextBoxCommand tb:
                    UserControls.Add(new TextBoxViewModel(tb, _userControlService, updateVarAction));
                    break;
                case DropdownOption dd:
                    UserControls.Add(new DropdownViewModel(dd, _userControlService, updateVarAction));
                    break;

                // Handle ButtonCommand with icon path resolution
                case ButtonCommand btn:
                    if (!string.IsNullOrEmpty(btn.IconPath))
                    {
                        btn.IconPath = ResolveIconPath(btn.IconPath, btn.Control);
                    }
                    UserControls.Add(new ButtonViewModel(btn));
                    break;
                default:
                    PostLogMessage($"[user-control] Warning: Unsupported control type: {control.GetType().Name}", true);
                    break;
            }
        }

        /// <summary>
        /// Resolves a relative icon path to an absolute URI.
        /// </summary>
        /// <param name="path"> the relative path to the icon file</param>
        /// <param name="controlName"> the name of the control</param>
        /// <returns> the absolute URI of the icon file, or null if not found</returns>
        private string? ResolveIconPath(string path, string controlName)
        {
            var iconFullPath = Path.Combine(AppContext.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(iconFullPath))
            {
                // Update to absolute URI format
                return new Uri(iconFullPath, UriKind.Absolute).AbsoluteUri;
            }
            else
            {
                PostLogMessage($"[user-control] Warning: Icon file not found for button '{controlName}': {iconFullPath}", true);
                return null; //clear invalid path
            }
        }
        #endregion

        #region User Variables

        /// <summary>
        /// Loads user-defined variable values and updates the corresponding user control view models with the retrieved
        /// data.
        /// </summary>
        /// <remarks>This method synchronizes the values of user controls with the latest user variable
        /// data. It should be called whenever user variables need to be refreshed or reloaded to ensure that the UI
        /// reflects the current state.</remarks>
        private void LoadUserVariables()
        {
            // Retrieve user variables for all defined controls
            var controls = UserControls.Select(vm => vm.Definition).ToList();
            // Load saved user variable values
            _userVariables = _userControlService.LoadUserVariables(controls);

            // Update each control's ViewModel with the loaded variable values
            foreach (var control in UserControls)
            {
                switch (control)
                {
                    case TextBoxViewModel tbVm:
                        var tbVar = _userVariables.FirstOrDefault(v => v.Id == tbVm.Id);
                        if (tbVar != null)
                        {
                            tbVm.Value = tbVar.Value;
                        }
                        break;
                    case DropdownViewModel ddVm:
                        var ddVar = _userVariables.FirstOrDefault(v => v.Id == ddVm.Id);
                        if (ddVar != null)
                        {
                            ddVm.SelectedValue = ddVar.Value;
                        }
                        break;
                }
            }
        }
        #endregion

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

            _imageSwitchLock?.Dispose();

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
