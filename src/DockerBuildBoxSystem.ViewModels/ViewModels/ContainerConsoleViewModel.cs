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

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public sealed record ConsoleLine(DateTime Timestamp, string Text, bool IsError);

    public sealed partial class ContainerConsoleViewModel : ViewModelBase
    {
        private readonly IContainerService _service;
        private readonly IClipboardService? _clipboard;

        //Canellation tokens
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

        public ObservableCollection<ConsoleLine> Lines { get; } = new ContainerObservableCollection<ConsoleLine>();
        public ObservableCollection<ContainerInfo> Containers { get; } = new();
        public ObservableCollection<UserCommand> UserCommands { get; } = new();

        [ObservableProperty]
        private string? _input;

        [ObservableProperty]

        private string _containerId = "";

        [ObservableProperty]
        private string _tail = "50";

        [ObservableProperty]
        private bool _isLogsRunning;

        [ObservableProperty]
        private bool _isCommandRunning;

        [ObservableProperty]
        private ContainerInfo? _selectedContainer;

        [ObservableProperty]
        private bool _showAllContainers = true;

        [ObservableProperty]
        private bool _isLoadingContainers;

        [ObservableProperty]
        private bool _autoStartLogs = true;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerConsoleViewModel"/> class.
        /// </summary>
        /// <param name="service">The container service used to interact with containers, ex Docker.</param>
        /// <param name="clipboard">Optional clipboard service for copying the text output.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public ContainerConsoleViewModel(IContainerService service, IClipboardService? clipboard = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _clipboard = clipboard;
            _syncContext = SynchronizationContext.Current;
        }

        [RelayCommand]
        private async Task InitializeAsync()
        {
            // Start the global UI update task
            StartUiUpdateTask();

            //load available containers on initialization
            await RefreshContainersCommand.ExecuteAsync(null);

            // Load user-defined commands
            await LoadUserCommandsAsync();

            //optionally auto-start logs if ContainerId is set
            if (AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
            {
                await StartLogsCommand.ExecuteAsync(null);
            }
        }

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
                        while (batch.Count < MaxLinesPerTick && _outputQueue.TryDequeue(out var line))
                        {
                            batch.Add(line);
                        }

                        if (batch.Count > 0)
                        {
                            PostBatchToUI(batch);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //If operation is canceled, flush any remaining items
                    while (!_outputQueue.IsEmpty)
                    {
                        var batch = new List<ConsoleLine>(MaxLinesPerTick);
                        while (batch.Count < MaxLinesPerTick && _outputQueue.TryDequeue(out var line))
                        {
                            batch.Add(line);
                        }
                        if (batch.Count == 0) break;
                        PostBatchToUI(batch);
                    }
                }
                finally
                {
                    uiTimer.Dispose();
                }
            }, ct);
        }

        private void PostBatchToUI(IReadOnlyList<ConsoleLine> batch)
        {
            if (batch.Count == 0) return;

            if (_syncContext != null)
            {
                _syncContext.Post(state =>
                {
                    var list = (IReadOnlyList<ConsoleLine>)state!;
                    AddLinesToUI(list);
                }, batch);
            }
            else
            {
                AddLinesToUI(batch);
            }
        }

        private void AddLinesToUI(IReadOnlyList<ConsoleLine> lines)
        {
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
        private void EnqueueLine(ConsoleLine line)
        {
            _outputQueue.Enqueue(line);
        }

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
                EnqueueLine(new ConsoleLine(DateTime.Now, $"[container-list-error] {ex.Message}", true));
            }
            finally
            {
                IsLoadingContainers = false;
            }
        }

        partial void OnSelectedContainerChanged(ContainerInfo? value)
        {
            if (value != null)
            {
                ContainerId = value.Id;
                EnqueueLine(new ConsoleLine(DateTime.Now, $"[info] Selected container: {value.Names.FirstOrDefault() ?? value.Id}", false));

                //auto start logs if enabled
                if (AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
                    _ = StartLogsCommand.ExecuteAsync(null);
            }
            else
            {
                ContainerId = "";
            }
        }

        partial void OnShowAllContainersChanged(bool value)
        {
            //auto refresh when this changes
            _ = RefreshContainersCommand.ExecuteAsync(null);
        }
        private bool CanSend() => !string.IsNullOrWhiteSpace(ContainerId) && !IsCommandRunning;

        /// <summary>
        /// Executes the specified user command asynchronously within the selected container.
        /// </summary>
        /// <remarks>This method sends the command to the selected container and captures the output,
        /// including standard output and error streams. The results are displayed in the UI. If no container is
        /// selected, the method logs a message indicating that no action was taken.</remarks>
        /// <param name="userCmd">The user command to execute. Can include the command and its arguments. If <paramref name="userCmd"/> is
        /// <see langword="null"/>, the method does not execute any command.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task RunUserCommandAsync(UserCommand? userCmd)
        {
            // Check if the user command or selected container is null
            if (userCmd is null || SelectedContainer is null)
            {
                EnqueueLine(new ConsoleLine(DateTime.Now, "[user-cmd] No command or container selected.", true));
                return;
            }

            var cmd = userCmd.Command;

            await ExecuteAndLogAsync(string.Join(' ', cmd));
        }

        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task SendAsync()
        {
            var cmd = (Input ?? String.Empty).Trim();
            //Clear the input after sending
            Input = string.Empty;
            await ExecuteAndLogAsync(cmd);
        }

        private async Task ExecuteAndLogAsync(string cmd)
        {
            //add command to console on UI thread
            EnqueueLine(new ConsoleLine(DateTime.Now, $"> {cmd}", false));

            //cancel any existing exec task
            _execCts?.Cancel();
            _execCts = new CancellationTokenSource();
            var ct = _execCts.Token;

            var containerInfo = await _service.InspectAsync(ContainerId, ct);

            //Whether to use TTY mode based on container settings
            bool useTty = containerInfo.Tty;

            //Execute with streaming output in background (fire-and-forget style)
            _execTask = Task.Run(async () =>
            {
                try
                {
                    var args = SplitShellLike(cmd);
                    var (output, exitCodeTask) = await _service.StreamExecAsync(ContainerId, args, tty: useTty, ct: ct);

                    //Stream the output line by line
                    await foreach (var (isErr, text) in output.ReadAllAsync(ct))
                    {
                        if (text is null) continue;
                        EnqueueLine(new ConsoleLine(DateTime.Now, text, isErr));
                    }

                    //Get the exit code after the stream completes
                    var exitCode = await exitCodeTask;
                    EnqueueLine(new ConsoleLine(DateTime.Now, $"[exit] {exitCode}", false));
                }
                catch (OperationCanceledException)
                {
                    EnqueueLine(new ConsoleLine(DateTime.Now, "[exec] canceled", false));
                }
                catch (Exception ex)
                {
                    EnqueueLine(new ConsoleLine(DateTime.Now, $"[exec-error] {ex.Message}", true));
                }
                finally
                {
                    IsCommandRunning = false;
                    UpdateCommandStates();
                }
            }, ct);

            IsCommandRunning = true;
            UpdateCommandStates();
        }

        private bool CanStartLogs() => !IsLogsRunning && !string.IsNullOrWhiteSpace(ContainerId);

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
                        EnqueueLine(new ConsoleLine(DateTime.Now, text, isErr));
                    }

                    _logsCts?.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    EnqueueLine(new ConsoleLine(DateTime.Now, "[logs] canceled", false));
                }
                catch (Exception ex)
                {
                    EnqueueLine(new ConsoleLine(DateTime.Now, $"[logs-error] {ex.Message}", true));
                }
            }, ct);

            IsLogsRunning = true;
            UpdateCommandStates();
        }

        private bool CanStopLogs() => IsLogsRunning;

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
                    UpdateCommandStates();
                }
            }
        }

        [RelayCommand]
        private Task ClearAsync()
        {
            Lines.Clear();
            return Task.CompletedTask;
        }

        /// <summary>
        /// The copy command to copy output of the container to clipboard.
        /// </summary>
        /// <returns></returns>
        [RelayCommand]
        private async Task CopyAsync()
        {
            var text = string.Join(Environment.NewLine, Lines.Select(l => $"{l.Timestamp:HH:mm:ss} {l.Text}"));
            if (_clipboard is not null)
            {
                await _clipboard.SetTextAsync(text);
            }
        }

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

        private static string[] SplitShellLike(string cmd)
        {
            //simple split
            return cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        private void UpdateCommandStates()
        {
            if (_syncContext == null)
            {
                NotifyAll();
                return;
            }

            _syncContext.Post(_ => NotifyAll(), null);

            void NotifyAll()
            {
                try
                {
                    SendCommand.NotifyCanExecuteChanged();
                    StartLogsCommand.NotifyCanExecuteChanged();
                    RunUserCommandCommand.NotifyCanExecuteChanged();
                    StopLogsCommand.NotifyCanExecuteChanged();
                }
                catch (InvalidOperationException)
                {
                    //protection in case things goes wrong, especially since this might be called during shutdown
                }
            }
        }

        partial void OnContainerIdChanged(string value)
        {
            UpdateCommandStates();
        }

        /// <summary>
        /// cancel and cleanup task
        /// </summary>
        /// <returns></returns>
        public override async ValueTask DisposeAsync()
        {
            await StopLogsAsync();

            _execCts?.Cancel();
            if (_execTask != null)
            {
                try
                {
                    await _execTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                //If operation is canceled or times out.. cur only proceeds to cleanup
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
                finally
                {
                    _execTask = null;
                    _execCts?.Dispose();
                    _execCts = null;
                }
            }

            await StopUiUpdateTaskAsync();
            await base.DisposeAsync();
        }
    }
}
