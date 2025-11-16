using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.Domain;
using DockerBuildBoxSystem.Models;
using DockerBuildBoxSystem.ViewModels.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly IClipboardService? _clipboard;
        private readonly ILogRunner _logRunner;
        private readonly ICommandRunner _cmdRunner;

        public readonly UILineBuffer UIHandler;

        // Manage user commands
        private readonly UserCommandService _userCommandService = new();
        /// <summary>
        /// Lines currently displayed in the console UI.
        /// </summary>
        public ObservableCollection<ConsoleLine> Lines { get; } = new ContainerObservableCollection<ConsoleLine>();

        /// <summary>
        /// List of available containers on the host.
        /// </summary>
        public ObservableCollection<ContainerInfo> Containers { get; } = new();

        /// <summary>
        /// User-defined commands
        /// </summary>
        public ObservableCollection<UserCommand> UserCommands { get; } = new();

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
        private string _containerId = "";

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
        ///     loads user commands, 
        ///     and optionally starts logs.
        /// </summary>
        [RelayCommand]
        private async Task InitializeAsync()
        {
            // Start the global UI update task
            UIHandler.Start();

            // Load available containers on initialization
            await RefreshContainersCommand.ExecuteAsync(null);

            // Load user-defined commands
            await LoadUserCommandsAsync();

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
                PostLogMessage($"[container-list-error] {ex.Message}", true);
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
            if (value != null)
            {
                ContainerId = value.Id;
                PostLogMessage($"[info] Selected container: {value.Names.FirstOrDefault() ?? value.Id}", false);

                //auto start logs if enabled
                if (AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
                    _ = StartLogsCommand.ExecuteAsync(null);
            }
            else
            {
                ContainerId = "";
            }
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
                PostLogMessage($"[info] Starting container: {ContainerId}", false);
                var status = await _service.StartAsync(ContainerId);
                if(status)
                {
                    PostLogMessage($"[info] Started container: {ContainerId}", false);
                }
                else
                {
                    PostLogMessage($"[start-container] Container did not start: {ContainerId}", true);
                }
            }
            catch (Exception ex)
            {
                PostLogMessage($"[start-container-error] {ex.Message}", true);
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
                PostLogMessage($"[info] Stopping container: {ContainerId}", false);
                await _service.StopAsync(ContainerId, timeout: TimeSpan.FromSeconds(10));
                PostLogMessage($"[info] Stopped container: {ContainerId}", false);
            }
            catch (Exception ex)
            {
                PostLogMessage($"[stop-container-error] {ex.Message}", true);
            }
            finally
            {
                _ = RefreshContainersCommand.ExecuteAsync(null);
            }
        }

        private bool CanRestartContainer() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == true);

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
                _ = RefreshContainersCommand.ExecuteAsync(null);
            }
        }

        #endregion

        #region Command Execution

        /// <summary>
        /// Determines whether sending commands is currently allowed.
        /// </summary>
        private bool CanSend() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == true);

        /// <summary>
        /// Executes the specified user command asynchronously within the selected container.
        /// </summary>
        /// <remarks>
        /// This method sends the command to the selected container and captures the output,
        /// including standard output and error streams. The results are displayed in the UI. If no container is
        /// selected, the method logs a message indicating that no action was taken.
        /// </remarks>
        /// <param name="userCmd">The user command to execute. Can include the command and its arguments. If <paramref name="userCmd"/>
        /// is <see langword="null"/>, the method does not execute any command.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task RunUserCommandAsync(UserCommand? userCmd)
        {
            // Check if the user command or selected container is null
            if (userCmd is null || SelectedContainer is null)
            {
                PostLogMessage("[user-cmd] No command or container selected.", true);
                return;
            }

            var cmd = userCmd.Command;

            await RouteInputAsync(string.Join(' ', cmd));
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
        private async Task ExecuteAndLogAsync(string cmd) => await ExecuteAndLogAsync(ShellSplitter.SplitShellLike(cmd));

        /// <summary>
        /// Executes a command inside the selected container and streams output to the console UI.
        /// </summary>
        /// <param name="args">The command and its arguments (argv form).</param>
        private async Task ExecuteAndLogAsync(string[] args)
        {
            //add command to console on UI thread
            PostLogMessage($"> {string.Join(' ', args)}", false);

            try
            {
                await foreach (var (isErr, line) in _cmdRunner.RunAsync(_service, ContainerId, args))
                {
                    UIHandler.EnqueueLine(line, isErr);
                }

                var exitCode = await _cmdRunner.ExitCode;
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

        #region Helpers

        /// <summary>
        /// Asynchronously loads user-defined commands from a JSON configuration file.
        /// </summary>
        /// <remarks>If the specified configuration file does not exist, a default set of commands is
        /// created, serialized to the file, and then loaded. The commands are deserialized into a list of <see
        /// cref="UserCommand"/> objects and added to the <c>UserCommands</c> collection.</remarks>
        /// <param name="filename">The name of the configuration file to load. Defaults to "commands.json".</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task LoadUserCommandsAsync(string filename = "commands.json")
        {
            // Determine the path to the configuration file
            var configPath = Path.Combine(AppContext.BaseDirectory, "Config", filename);
            // If the file does not exist, create it with default commands
            if (!File.Exists(configPath))
            {
                var defaultCmds = new[]
                {
                    new UserCommand { Label = "List /", Command = ["ls", "/"] },
                    new UserCommand { Label = "Check Disk", Command = ["df", "-h"] },
                };
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                await File.WriteAllTextAsync(configPath,
                    JsonSerializer.Serialize(defaultCmds, new JsonSerializerOptions { WriteIndented = true }));
            }
            // Read and deserialize the commands from the configuration file
            var json = await File.ReadAllTextAsync(configPath);
            var cmds = JsonSerializer.Deserialize<List<UserCommand>>(json) ?? new List<UserCommand>();
            UserCommands.Clear();
            // Add each command to the UserCommands collection
            foreach (var cmd in cmds)
            {
                UserCommands.Add(cmd);
            }
        }


        public async Task AddCommandAsync(UserCommand newCmd)
        {
            await _userCommandService.AddAsync(newCmd);
            UserCommands.Add(newCmd);
        }

        public async Task RemoveCommandAtAsync(int index)
        {
            await _userCommandService.RemoveAtAsync(index);
            UserCommands.RemoveAt(index);
        }


        private async Task RouteInputAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            if (await _cmdRunner.TryWriteToInteractiveAsync(raw))
                return;

            var args = ShellSplitter.SplitShellLike(raw);
            await ExecuteAndLogAsync(args);
        }

        private void PostLogMessage(string message, bool isError, bool isImportant = false) => UIHandler.EnqueueLine(message + "\r\n", isError, isImportant);
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
