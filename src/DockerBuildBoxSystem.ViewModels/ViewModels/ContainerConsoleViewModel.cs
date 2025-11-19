using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.Domain;
using DockerBuildBoxSystem.Models;
using DockerBuildBoxSystem.ViewModels.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{

    /// <summary>
    /// ViewModel for a container console that streams logs and executes commands inside Docker containers.
    /// </summary>
    public sealed partial class ContainerConsoleViewModel : ViewModelBase
    {
        private readonly IContainerService _service;
        private readonly IClipboardService? _clipboard;
        private readonly ILogRunner _logRunner;
        private readonly ICommandRunner _cmdRunner;

        public readonly UILineBuffer UIHandler;

        /// <summary>
        /// Lines currently displayed in the console UI.
        /// </summary>
        public ObservableCollection<ConsoleLine> Lines { get; } = new ContainerObservableCollection<ConsoleLine>();

        /// <summary>
        /// List of available containers on the host.
        /// </summary>
        public ObservableCollection<ContainerInfo> Containers { get; } = new();

        /// <summary>
        /// User-defined controls
        /// </summary>
        public ObservableCollection<UserControlDefinition> UserControlDefinition { get; } = new();

        /// <summary>
        /// The raw user input text bound from the UI.
        /// </summary>
        [ObservableProperty]
        private string? _input;

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
        private string _containerId = "";

        public bool CanUseUserControls => CanSend();

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
        private ContainerInfo? _selectedContainer;

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

        // Track previous selected container id to manage stop-on-switch behavior
        private string? _previousContainerId;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerConsoleViewModel"/> class.
        /// </summary>
        /// <param name="service">The container service used to interact with containers, ex Docker.</param>
        /// <param name="clipboard">Optional clipboard service for copying the text output.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="service"/> is null.</exception>
        public ContainerConsoleViewModel(IContainerService service, IClipboardService? clipboard = null) : base()
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _clipboard = clipboard;

            _logRunner = new LogRunner();
            _cmdRunner = new CommandRunner();
            _cmdRunner.RunningChanged += (_, __) =>
                SetOnUiThread(() =>
                {
                    OnPropertyChanged(nameof(IsCommandRunning));
                    SendCommand.NotifyCanExecuteChanged();
                    RunUserCommandCommand.NotifyCanExecuteChanged();
                    StopExecCommand.NotifyCanExecuteChanged();
                });

            _logRunner.RunningChanged += (_, __) =>
                SetOnUiThread(() =>
                {
                    OnPropertyChanged(nameof(IsLogsRunning));
                    StartLogsCommand.NotifyCanExecuteChanged();
                    StopLogsCommand.NotifyCanExecuteChanged();
                });

            UIHandler = new UILineBuffer(Lines);
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

        #region Container Management
        /// <summary>
        /// Refreshes the list of containers from the container service.
        /// </summary>
        [RelayCommand]
        private async Task RefreshContainersAsync()
        {
            var selectedContainerId = SelectedContainer?.Id;
            IsLoadingContainers = true;
            try
            {
                //not using ConfigureAwait(false) since we want to return to the UI thread as soon as possible (no stalling :))
                var containers = await _service.ListContainersAsync(all: ShowAllContainers);

                //Back to the UI threa so safe to update ObservableCollection
                Containers.Clear();
                foreach (var container in containers)
                {
                    if(string.Compare(container.Id, selectedContainerId) == 0)
                    {
                        SelectedContainer = container;
                    }
                    Containers.Add(container);
                }
            }
            catch (Exception ex)
            {
                UIHandler.EnqueueLine($"[container-list-error] {ex.Message}", true);
            }
            finally
            {
                IsLoadingContainers = false;
            }
        }

        /// <summary>
        /// Updates dependent state when the selected container changes.
        /// </summary>
        /// <param name="value">The newly selected container info or null.</param>
        partial void OnSelectedContainerChanged(ContainerInfo? value)
        {
            var newContainer = value;
            var oldId = _previousContainerId ?? ContainerId; // fallback to current if previous not tracked yet

            if (newContainer != null)
            {
                // If switching to a DIFFERENT container and previous was running, stop it
                if (!string.IsNullOrWhiteSpace(oldId) && oldId != newContainer.Id)
                {
                    var prev = Containers.FirstOrDefault(c => c.Id == oldId);
                    if (prev?.IsRunning == true)
                    {
                        _ = StopContainerByIdAsync(oldId);
                    }
                }

                ContainerId = newContainer.Id;
                UIHandler.EnqueueLine($"[info] Selected container: {newContainer.Names.FirstOrDefault() ?? newContainer.Id}", false);

                //auto start logs if enabled
                if (AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
                    _ = StartLogsCommand.ExecuteAsync(null);
            }
            else
            {
                ContainerId = string.Empty;
            }

            _previousContainerId = ContainerId; // update tracker (after change)
        }

        /// <summary>
        /// Invoked when the value of the "Show All Containers" setting changes.
        /// </summary>
        /// <param name="value">The new value of the "Show All Containers" setting.
        /// <see langword="true"/> if all containers should be shown; otherwise, <see langword="false"/>.</param>
        partial void OnShowAllContainersChanged(bool value) => RefreshContainersCommand.ExecuteAsync(null);
        
        private bool CanStartContainer() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == false);

        /// <summary>
        /// Starts the selected container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartContainer))]
        private async Task StartContainerAsync()
        {
            if (string.IsNullOrWhiteSpace(ContainerId)) return;
            try
            {
                UIHandler.EnqueueLine($"[info] Starting container: {ContainerId}", false);
                var status = await _service.StartAsync(ContainerId);
                if(status)
                {
                    UIHandler.EnqueueLine($"[info] Started container: {ContainerId}", false);
                }
                else
                {
                    UIHandler.EnqueueLine($"[start-container] Container did not start: {ContainerId}", true);
                }
            }
            catch (Exception ex)
            {
                UIHandler.EnqueueLine($"[start-container-error] {ex.Message}", true);
            }
            finally
            {
                _ = RefreshContainersCommand.ExecuteAsync(null);
            }
        }

        private bool CanStopContainer() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == true);

        /// <summary>
        /// Stops a running container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopContainer))]
        private async Task StopContainerAsync()
        {
            if (string.IsNullOrWhiteSpace(ContainerId)) return;
            try
            {
                UIHandler.EnqueueLine($"[info] Stopping container: {ContainerId}", false);
                await _service.StopAsync(ContainerId, timeout: TimeSpan.FromSeconds(10));
                UIHandler.EnqueueLine($"[info] Stopped container: {ContainerId}", false);
            }
            catch (Exception ex)
            {
                UIHandler.EnqueueLine($"[stop-container-error] {ex.Message}", true);
            }
            finally
            {
                _ = RefreshContainersCommand.ExecuteAsync(null);
            }
        }

        private bool CanRestartContainer() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == true);

        /// <summary>
        /// Stops a container by id (used when auto-stopping previous selection).
        /// </summary>
        private async Task StopContainerByIdAsync(string id)
        {
            try
            {
                var prev = Containers.FirstOrDefault(c => c.Id == id);
                var nameOrId = prev?.Names.FirstOrDefault() ?? id;
                UIHandler.EnqueueLine($"[info] Auto-stopping previous container: {nameOrId}", false);
                await _service.StopAsync(id, timeout: TimeSpan.FromSeconds(10));
                UIHandler.EnqueueLine($"[info] Auto-stopped container: {nameOrId}", false);
            }
            catch (Exception ex)
            {
                UIHandler.EnqueueLine($"[auto-stop-error] {ex.Message}", true);
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
                UIHandler.EnqueueLine($"[info] Restarting container: {ContainerId}", false);
                await _service.RestartAsync(ContainerId, timeout: TimeSpan.FromSeconds(10));
                UIHandler.EnqueueLine($"[info] Restarted container: {ContainerId}", false);
            }
            catch (Exception ex)
            {
                UIHandler.EnqueueLine($"[restart-container-error] {ex.Message}", true);
            }
            finally
            {
                _ = RefreshContainersCommand.ExecuteAsync(null);
            }
        }

        #endregion

        #region Command Execution

        /// <summary>
        /// Determines whether sending commands is currently allowed.
        /// </summary>
        private bool CanSend() => !string.IsNullOrWhiteSpace(ContainerId) && !_cmdRunner.IsRunning && (SelectedContainer?.IsRunning == true);

     
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
                UIHandler.EnqueueLine("[user-cmd] No command or container selected.", true);
                return;
            }
            // Handle ButtonCommand control type
            if (control is ButtonCommand buttonCmd)
            {
                var cmds = buttonCmd.Command;
                if (cmds.Length == 0)
                {
                    UIHandler.EnqueueLine("[user-cmd] No command defined for this button.", true);
                    return;
                }
                var cmdStr = string.Join(' ', cmds);
                await ExecuteAndLogAsync(cmdStr);
            }
            else
            {
                UIHandler.EnqueueLine("[user-cmd] Unsupported control type for command execution.", true);
            }
        }

        /// <summary>
        /// Sends the current input text as a command to the container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task SendAsync()
        {
            var cmd = (Input ?? String.Empty).Trim();
            //Clear the input after sending
            Input = string.Empty;
            await ExecuteAndLogAsync(cmd);
        }

        /// <summary>
        /// Executes a command string by splitting it as a argv array.
        /// </summary>
        private async Task ExecuteAndLogAsync(string cmd) => await ExecuteAndLogAsync(ShellSplitter.SplitShellLike(cmd));

        /// <summary>
        /// Executes a command inside the selected container and streams output to the console UI.
        /// </summary>
        /// <param name="args">The command and its arguments (argv form).</param>
        private async Task ExecuteAndLogAsync(string[] args)
        {
            UIHandler.EnqueueLine($"> {string.Join(' ', args)}", false);

            try
            {
                await foreach (var (isErr, line) in _cmdRunner.RunAsync(_service, ContainerId, args))
                {
                    UIHandler.EnqueueLine(line, isErr);
                }

                var exitCode = await _cmdRunner.ExitCode;
                UIHandler.EnqueueLine($"[exit] {exitCode}", false, true);
            }
            catch (OperationCanceledException)
            {
                UIHandler.EnqueueLine("[exec] canceled", false, true);
            }
            catch (Exception ex)
            {
                UIHandler.EnqueueLine($"[exec-error] {ex.Message}", true, true);
            }
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
        private async Task StartLogsAsync()
        {
            try
            {
                await foreach (var (isErr, line) in _logRunner.RunAsync(_service, ContainerId))
                {
                    UIHandler.EnqueueLine(line, isErr);
                }
            }
            catch (OperationCanceledException)
            {
                UIHandler.EnqueueLine("[logs] canceled", false);
            }
            catch (Exception ex)
            {
                UIHandler.EnqueueLine($"[logs-error] {ex.Message}", true, true);
            }
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
        private Task ClearAsync()
        {
            UIHandler.ClearAsync();
            return Task.CompletedTask;
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

        #region Helpers

        /// <summary>
        /// Asynchronously loads user control definitions from a JSON configuration file.
        /// </summary>
        /// <remarks>This method reads the specified JSON file, validates its content, and deserializes it
        /// into a list of user control definitions. If the file does not exist, a warning message is logged, and the
        /// method exits. The method enforces a maximum of 7 controls; if more are defined, only the first 7 are loaded,
        /// and a warning is logged. For button controls with an icon path, the method verifies the existence of the
        /// icon file and updates the path to an absolute URI if valid. If the icon file is missing, a warning is
        /// logged, and the icon path is cleared.</remarks>
        /// <param name="filename">The name of the configuration file to load. Defaults to "commands.json".</param>
        /// <returns></returns>
        private async Task LoadUserControlsAsync(string filename = "commands.json")
        {
            // Determine the path to the configuration file
            var configPath = Path.Combine(AppContext.BaseDirectory, "Config", filename);
            if (!File.Exists(configPath))
            {
                UIHandler.EnqueueLine($"[user-control] Configuration file not found: {configPath}", true);
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new UserControlConverter() }
                };

                // Validate and deserialize the JSON content
                using var jsonDoc = JsonDocument.Parse(json);
                var controls = JsonSerializer.Deserialize<List<UserControlDefinition>>(json, options)!;

                // Limit to maximum of 7 controls
                if (controls.Count > 7)
                {
                    UIHandler.EnqueueLine("[user-control] Warning: More than 7 controls defined. Only the first 7 will be loaded.", true);
                    controls = controls.Take(7).ToList();
                }
                // Clear existing controls and add the new ones
                UserControlDefinition.Clear();
                foreach (var c in controls ?? [])
                {
                    // For ButtonCommand, validate and update icon path
                    if (c is ButtonCommand btn && !string.IsNullOrEmpty(btn.IconPath))
                    {
                        // Resolve the full path to the icon file
                        var iconFullPath = Path.Combine(AppContext.BaseDirectory, btn.IconPath.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(iconFullPath))
                        {
                            // Update to absolute URI format
                            btn.IconPath = new Uri(iconFullPath, UriKind.Absolute).AbsoluteUri;
                        }
                        else
                        {
                            UIHandler.EnqueueLine($"[user-control] Warning: Icon file not found for button '{btn.Control}': {iconFullPath}", true);
                            btn.IconPath = null; //clear invalid path
                        }
                    }
                    // Add the control to the collection
                    UserControlDefinition.Add(c);
                }
            }
            catch (JsonException jex)
            {
                UIHandler.EnqueueLine($"[user-control] JSON parsing error: {jex.Message}", true);
            }
            catch (Exception ex)
            {
                UIHandler.EnqueueLine($"[user-control] Error loading user controls: {ex.Message}", true);
            }
        }


        /// <summary>
        /// Retrieves the platform selection from the dropdown options.
        /// </summary>
        /// <remarks>This method searches the dropdown options for an entry with an ID matching "platform"
        /// (case-insensitive). If no such option exists, the method returns <see langword="null"/>.</remarks>
        /// <returns>The <see cref="DropdownOption"/> representing the platform selection, or <see langword="null"/>  if no
        /// matching option is found.</returns>
        private DropdownOption? GetPlatformFromDropdown()
        {
            // To use in the build process when the target platform is needed
            return UserControlDefinition
                .OfType<DropdownOption>()
                .FirstOrDefault(d => d.Id.Equals("platform", StringComparison.OrdinalIgnoreCase));
        }


        #endregion

        #region Cleanup

        /// <summary>
        /// cancel and cleanup task
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            await StopLogsAsync();
            await StopExecAsync();
            await UIHandler.StopAsync();
            await base.DisposeAsync();
        }

        #endregion
    }
}
