using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using DockerBuildBoxSystem.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockerBuildBoxSystem.Domain;

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

        //logs streaming
        private CancellationTokenSource? _logsCts;
        private Task? _logStreamTask;

        //exec streaming
        private CancellationTokenSource? _execCts;
        private Task? _execTask;

        //To ensure UI is responsive
        private const int MaxConsoleLines = 2000;
        private const int MaxLinesPerTick = 200;

        //global UI update
        private readonly ConcurrentQueue<ConsoleLine> _outputQueue = new();
        private CancellationTokenSource? _uiUpdateCts;
        private Task? _uiUpdateTask;
        private readonly SynchronizationContext? _syncContext;

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
            StartUiUpdateTask();

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
                EnqueueLine("[user-cmd] No command or container selected.", true);
                return;
            }
            // Handle ButtonCommand control type
            if (control is ButtonCommand buttonCmd)
            {
                var cmds = buttonCmd.Command;
                if (cmds.Length == 0)
                {
                    EnqueueLine("[user-cmd] No command defined for this button.", true);
                    return;
                }
                var cmdStr = string.Join(' ', cmds);
                await ExecuteAndLogAsync(cmdStr);
            }
            else
            {
                EnqueueLine("[user-cmd] Unsupported control type for command execution.", true);
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
        private async Task ExecuteAndLogAsync(string cmd) => await ExecuteAndLogAsync(SplitShellLike(cmd));

        /// <summary>
        /// Executes a command inside the selected container and streams output to the console UI.
        /// </summary>
        /// <param name="args">The command and its arguments (argv form).</param>
        private async Task ExecuteAndLogAsync(string[] args)
        {
            //cancel any existing exec task
            _execCts?.Cancel();
            _execCts = new CancellationTokenSource();
            var ct = _execCts.Token;

            var containerInfo = await _service.InspectAsync(ContainerId, ct);

            //Whether to use TTY mode based on container settings
            bool useTty = containerInfo.Tty;

            //add command to console on UI thread
            EnqueueLine($"> {string.Join(' ', args)}", false);

            //Execute with streaming output in background (fire-and-forget style)
            _execTask = Task.Run(async () =>
            {
                try
                {
                    var (output, exitCodeTask) = await _service.StreamExecAsync(ContainerId, args, tty: useTty, ct: ct);

                    //Stream the output line by line
                    await foreach (var (isErr, text) in output.ReadAllAsync(ct))
                    {
                        if (text is null) continue;
                        EnqueueLine(text, isErr);
                    }

                    //Get the exit code after the stream completes
                    var exitCode = await exitCodeTask;
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
            }, ct);

            SetOnUiThread(() => IsCommandRunning = true);
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
            _execCts?.Cancel();

            if (_execTask is not null)
            {
                try
                {
                    // Await with a timeout to prevent hanging during shutdown
                    await _execTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
                finally
                {
                    _execTask = null;
                    _execCts?.Dispose();
                    _execCts = null;

                    SetOnUiThread(() => IsCommandRunning = false);
                }
            }
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
            //Stop existing logs if it is currently running
            await StopLogsAsync();

            _logsCts = new();
            var ct = _logsCts.Token;

            var containerInfo = await _service.InspectAsync(ContainerId, ct);

            //Whether to use TTY mode based on container settings
            bool useTty = containerInfo.Tty;

            _logStreamTask = Task.Run(async () =>
            {
                try
                {
                    var reader = await _service.StreamLogsAsync(
                        ContainerId,
                        follow: true,
                        tail: Tail,
                        tty: useTty,
                        ct: ct);

                    await foreach (var (isErr, text) in reader.ReadAllAsync(ct))
                    {
                        if (text is null) continue;
                        EnqueueLine(text, isErr);
                    }

                    _logsCts?.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    EnqueueLine("[logs] canceled", false);
                }
                catch (Exception ex)
                {
                    EnqueueLine($"[logs-error] {ex.Message}", true, true);
                }
            }, ct);

            IsLogsRunning = true;
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
            _logsCts?.Cancel();

            if (_logStreamTask is not null)
            {
                try
                {
                    //await with a timeout to prevent hanging during shutdown
                    await _logStreamTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
                finally
                {
                    _logStreamTask = null;
                    _logsCts?.Dispose();
                    _logsCts = null;

                    IsLogsRunning = false;
                }
            }
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
                EnqueueLine($"[user-control] Configuration file not found: {configPath}", true);
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
                    EnqueueLine("[user-control] Warning: More than 7 controls defined. Only the first 7 will be loaded.", true);
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
                            EnqueueLine($"[user-control] Warning: Icon file not found for button '{btn.Control}': {iconFullPath}", true);
                            btn.IconPath = null; //clear invalid path
                        }
                    }
                    // Add the control to the collection
                    UserControlDefinition.Add(c);
                }
            }
            catch (JsonException jex)
            {
                EnqueueLine($"[user-control] JSON parsing error: {jex.Message}", true);
            }
            catch (Exception ex)
            {
                EnqueueLine($"[user-control] Error loading user controls: {ex.Message}", true);
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
