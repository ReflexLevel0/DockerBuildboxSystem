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
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    #region UI/Presentation DTOs
    /// <summary>
    /// Represents a console line, containing metadata
    /// </summary>
    /// <param name="Timestamp">The time at which the line was produced.</param>
    /// <param name="Text">The line text.</param>
    /// <param name="IsError">True if the line represents an error output.</param>
    /// <param name="IsImportant">True if the line is considered important (e.g., should trigger auto-scroll).</param>
    public sealed record ConsoleLine(DateTime Timestamp, string Text, bool IsError, bool IsImportant = false);
    #endregion

    /// <summary>
    /// ViewModel for a container console that streams logs and executes commands inside Docker containers.
    /// </summary>
    public sealed partial class ContainerConsoleViewModel : ViewModelBase
    {
        private readonly IContainerService _service;
        private readonly IClipboardService? _clipboard;
        private readonly ILogRunner _logRunner;
        private readonly ICommandRunner _cmdRunner;

        //To ensure UI is responsive
        private const int MaxConsoleLines = 2000;
        private const int MaxLinesPerTick = 200;

        //global UI update
        private readonly ConcurrentQueue<ConsoleLine> _outputQueue = new();
        private CancellationTokenSource? _uiUpdateCts;
        private Task? _uiUpdateTask;
        private readonly SynchronizationContext? _syncContext;

        // Manage user commands
        private readonly UserCommandService _userCommandService = new();

        /// <summary>
        /// Raised whenever an important line is added to the UI, ex a error line that needs attention.
        /// Used for view-specific behaviors (e.g., auto-scroll).
        /// </summary>
        public event EventHandler<ConsoleLine>? ImportantLineArrived;

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
        /// How many lines to tail when starting logs ("all" or a number).
        /// </summary>
        [ObservableProperty]
        private string _tail = "50";

        /// <summary>
        /// True while logs are currently being streamed.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartLogsCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopLogsCommand))]
        private bool _isLogsRunning;

        /// <summary>
        /// True while a command is being executed.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunUserCommandCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopExecCommand))]
        private bool _isCommandRunning;

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
        /// Repreents a batch for the UI, comprised of two subsets: general lines that are posted to the UI, and important lines
        /// that might need some special handling (e.g., auto-scroll in the view). Only defined internally 
        /// </summary>
        private sealed record UiBatch(IReadOnlyList<ConsoleLine> Lines, IReadOnlyList<ConsoleLine> Important);

        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerConsoleViewModel"/> class.
        /// </summary>
        /// <param name="service">The container service used to interact with containers, ex Docker.</param>
        /// <param name="clipboard">Optional clipboard service for copying the text output.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="service"/> is null.</exception>
        public ContainerConsoleViewModel(IContainerService service, IClipboardService? clipboard = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _clipboard = clipboard;
            _syncContext = SynchronizationContext.Current;

            _logRunner = new LogRunner();
            _cmdRunner = new CommandRunner();
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
            StartUiUpdateTask();

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

        #region UI Update Mechanism

        /// <summary>
        /// Starts the global UI update task that processes the output queue.
        /// Separated to avoid multiple concurrent UI updates.
        /// </summary>
        private void StartUiUpdateTask()
        {
            if (_uiUpdateTask != null)
                return; //if it is already running

            _uiUpdateCts = new CancellationTokenSource();
            var ct = _uiUpdateCts.Token;

            var uiTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));

            _uiUpdateTask = Task.Run(async () =>
            {
                try
                {
                    while (await uiTimer.WaitForNextTickAsync(ct))
                    {
                        //Restrict the number of lines per tick
                        //If we dequeue everything at once it might overwhelm the UI
                        var batch = new List<ConsoleLine>(MaxLinesPerTick);
                        var important = new List<ConsoleLine>();
                        while (batch.Count < MaxLinesPerTick && _outputQueue.TryDequeue(out var line))
                        {
                            batch.Add(line);
                            if (line.IsImportant)
                                important.Add(line);
                        }

                        if (batch.Count > 0)
                        {
                            PostBatchToUI(new UiBatch(batch, important));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //If operation is canceled, flush any remaining items
                    while (!_outputQueue.IsEmpty)
                    {
                        var batch = new List<ConsoleLine>(MaxLinesPerTick);
                        var important = new List<ConsoleLine>();
                        while (batch.Count < MaxLinesPerTick && _outputQueue.TryDequeue(out var line))
                        {
                            batch.Add(line);
                            if (line.IsImportant)
                                important.Add(line);
                        }
                        if (batch.Count == 0) break;
                        PostBatchToUI(new UiBatch(batch, important));
                    }
                }
                finally
                {
                    uiTimer.Dispose();
                }
            }, ct);
        }

        /// <summary>
        /// Posts a batch to the UI thread (OR directly if no synchronization context is available).
        /// </summary>
        /// <param name="batch">The batch of console lines to append to the UI.</param>
        private void PostBatchToUI(UiBatch batch)
        {
            if (batch.Lines.Count == 0) return;

            if (_syncContext != null)
            {
                _syncContext.Post(state =>
                {
                    var b = (UiBatch)state!;
                    AddLinesToUI(b);
                }, batch);
            }
            else
            {
                AddLinesToUI(batch);
            }
        }

        /// <summary>
        /// Appends a batch of lines to the bound collection.
        /// Trims the collection to keep the UI responsive.
        /// Triggers <see cref="ImportantLineArrived"/> for any important lines in the batch.
        /// </summary>
        /// <param name="batch">The batch of lines to add.</param>
        private void AddLinesToUI(UiBatch batch)
        {
            var lines = batch.Lines;
            if (lines.Count == 0) return;

            if (Lines is ContainerObservableCollection<ConsoleLine> contLines)
            {
                contLines.AddRange(lines);
            }
            else
            {
                foreach (var line in lines)
                {
                    Lines.Add(line);
                }
            }

            //Trim the UI by removing old lines - why? keep it responsive! otherwise... lags
            if (Lines.Count > MaxConsoleLines)
            {
                var toRemove = Lines.Count - MaxConsoleLines;
                //remove from the start
                for (int i = 0; i < toRemove; i++)
                {
                    Lines.RemoveAt(0);
                }
            }

            //nnbotify only for the important lines captured during batching
            foreach (var l in batch.Important)
            {
                ImportantLineArrived?.Invoke(this, l);
            }
        }

        /// <summary>
        /// Stops the UI update task.
        /// </summary>
        private async Task StopUiUpdateTaskAsync()
        {
            _uiUpdateCts?.Cancel();

            if (_uiUpdateTask != null)
            {
                try
                {
                    await _uiUpdateTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
                finally
                {
                    _uiUpdateTask = null;
                    _uiUpdateCts?.Dispose();
                    _uiUpdateCts = null;
                }
            }
        }

        /// <summary>
        /// Enqueues a line to be added to the console output on the UI thread.
        /// </summary>
        /// <param name="line">The console line to enqueue.</param>
        private void EnqueueLine(ConsoleLine line)
        {
            _outputQueue.Enqueue(line);
        }

        /// <summary>
        /// Creates and enqueues a console line with the provided text and flags.
        /// </summary>
        /// <param name="text">Text to append.</param>
        /// <param name="isError">Whether the line is an error.</param>
        /// <param name="isImportant">Whether the line is important.</param>
        private void EnqueueLine(string text, bool isError, bool isImportant = false)
        {
            EnqueueLine(new ConsoleLine(DateTime.Now, text, isError, isImportant));
        }

        #endregion

        #region Container Management
        /// <summary>
        /// Refreshes the list of containers from the container service.
        /// </summary>
        [RelayCommand]
        private async Task RefreshContainersAsync()
        {
            IsLoadingContainers = true;
            try
            {
                //not using ConfigureAwait(false) since we want to return to the UI thread as soon as possible (no stalling :))
                var containers = await _service.ListContainersAsync(all: ShowAllContainers);

                //Back to the UI threa so safe to update ObservableCollection
                Containers.Clear();
                foreach (var container in containers)
                {
                    Containers.Add(container);
                }
            }
            catch (Exception ex)
            {
                EnqueueLine($"[container-list-error] {ex.Message}", true);
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
                EnqueueLine($"[info] Selected container: {value.Names.FirstOrDefault() ?? value.Id}", false);

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
        partial void OnShowAllContainersChanged(bool value)
        {
            //auto refresh when this changes
            _ = RefreshContainersCommand.ExecuteAsync(null);
        }

        
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
                EnqueueLine($"[info] Starting container: {ContainerId}", false);
                var status = await _service.StartAsync(ContainerId);
                if(status)
                {
                    EnqueueLine($"[info] Started container: {ContainerId}", false);
                }
                else
                {
                    EnqueueLine($"[start-container] Container did not start: {ContainerId}", true);
                }
            }
            catch (Exception ex)
            {
                EnqueueLine($"[start-container-error] {ex.Message}", true);
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
                EnqueueLine($"[info] Stopping container: {ContainerId}", false);
                await _service.StopAsync(ContainerId, timeout: TimeSpan.FromSeconds(10));
                EnqueueLine($"[info] Stopped container: {ContainerId}", false);
            }
            catch (Exception ex)
            {
                EnqueueLine($"[stop-container-error] {ex.Message}", true);
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
                EnqueueLine($"[info] Restarting container: {ContainerId}", false);
                await _service.RestartAsync(ContainerId, timeout: TimeSpan.FromSeconds(10));
                EnqueueLine($"[info] Restarted container: {ContainerId}", false);
            }
            catch (Exception ex)
            {
                EnqueueLine($"[restart-container-error] {ex.Message}", true);
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
        private bool CanSend() => !string.IsNullOrWhiteSpace(ContainerId) && !IsCommandRunning && (SelectedContainer?.IsRunning == true);

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
                EnqueueLine("[user-cmd] No command or container selected.", true);
                return;
            }

            var cmd = userCmd.Command;

            await ExecuteAndLogAsync(cmd);
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
        private async Task ExecuteAndLogAsync(string cmd) => await ExecuteAndLogAsync(SplitShellLike(cmd));

        /// <summary>
        /// Executes a command inside the selected container and streams output to the console UI.
        /// </summary>
        /// <param name="args">The command and its arguments (argv form).</param>
        private async Task ExecuteAndLogAsync(string[] args)
        {
            EnqueueLine($"> {string.Join(' ', args)}", false);

            SetOnUiThread(() => IsCommandRunning = true);
            try
            {
                await foreach (var (line, isErr) in _cmdRunner.RunAsync(_service, ContainerId, args))
                {
                    EnqueueLine(isErr, line);
                }

                var exitCode = _cmdRunner.ExitCode;
                EnqueueLine($"[exit] {exitCode}", false, true);
            }
            catch (OperationCanceledException)
            {
                EnqueueLine("[exec] canceled", false, true);
            }
            catch (Exception ex)
            {
                EnqueueLine($"[exec-error] {ex.Message}", true, true);
            }
            finally
            {
                SetOnUiThread(() => IsCommandRunning = false);
            }
        }

        /// <summary>
        /// Determines whether command execution can be stopped.
        /// </summary>
        private bool CanStopExec() => IsCommandRunning;

        /// <summary>
        /// Stops the current command execution task, if it is running.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopExec))]
        private async Task StopExecAsync()
        {
            await _cmdRunner.StopAsync();
            SetOnUiThread(() => IsCommandRunning = false);
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
            SetOnUiThread(() => IsLogsRunning = true);

            try
            {
                await foreach (var (line, isErr) in _logRunner.RunAsync(_service, ContainerId))
                {
                    EnqueueLine(isErr, line);
                }
            }
            catch (OperationCanceledException)
            {
                EnqueueLine("[logs] canceled", false);
            }
            catch (Exception ex)
            {
                EnqueueLine($"[logs-error] {ex.Message}", true, true);
            }
            finally
            {
                SetOnUiThread(() => IsLogsRunning = false);
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
            SetOnUiThread(() => IsLogsRunning = false);
        }

        #endregion

        #region Console Management

        /// <summary>
        /// Clears all lines from the console.
        /// </summary>
        [RelayCommand]
        private Task ClearAsync()
        {
            Lines.Clear();
            return Task.CompletedTask;
        }

        /// <summary>
        /// The copy command to copy output of the container to clipboard.
        /// </summary>
        [RelayCommand]
        private async Task CopyAsync()
        {
            var text = string.Join(Environment.NewLine, Lines.Select(l => $"{l.Timestamp:HH:mm:ss} {l.Text}"));
            if (_clipboard is not null)
            {
                await _clipboard.SetTextAsync(text);
            }
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

        /// <summary>
        /// Splits a shell-like command string into argv tokens (very simple splitting by spaces
        /// </summary>
        /// <param name="cmd">The command string.</param>
        /// <returns>The argv array.</returns>
        private static string[] SplitShellLike(string cmd)
        {
            //simple split
            return cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Executes an action on the UI thread IF a synchronization context is available, otherwise executes it inline.
        /// </summary>
        private void SetOnUiThread(Action action)
        {
            if (_syncContext == null || SynchronizationContext.Current == _syncContext)
            {
                action();
                return;
            }

            _syncContext.Post(_ =>
            {
                try { 
                    action(); 
                }
                catch (InvalidOperationException) { 
                
                }
            }, null);
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
            await StopUiUpdateTaskAsync();
            await base.DisposeAsync();
        }

        #endregion
    }
}
