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

        private CancellationTokenSource? _cts;
        private Task? _logStreamTask;
        private Task? _uiUpdateTask;
        private readonly SynchronizationContext? _syncContext;

        public ObservableCollection<ConsoleLine> Lines { get; } = new();
        public ObservableCollection<ContainerInfo> Containers { get; } = new();
        public ObservableCollection<UserCommand> UserCommands { get; } = new();

        [ObservableProperty]
        private string? _input;

        [ObservableProperty]

        private string _containerId = "";

        [ObservableProperty]
        private string _tail = "50";

        [ObservableProperty]
        private bool _tty;

        [ObservableProperty]
        private bool _isLogsRunning;

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
            _ = LoadUserCommandsAsync();
        }

        [RelayCommand]
        private async Task InitializeAsync()
        {
            //load available containers on initialization
            await RefreshContainersCommand.ExecuteAsync(null);

            //optionally auto-start logs if ContainerId is set
            if (AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
            {
                await StartLogsCommand.ExecuteAsync(null);
            }
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
                AddLineToUI(new ConsoleLine(DateTime.Now, $"[container-list-error] {ex.Message}", true));
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
                AddLineToUI(new ConsoleLine(DateTime.Now, $"[info] Selected container: {value.Names.FirstOrDefault() ?? value.Id}", false));

                //auto start logs if enabled
                if(AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
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

        private bool CanSend() => !string.IsNullOrWhiteSpace(ContainerId);

        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task SendAsync()
        {
            var cmd = (Input ?? "").Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            //add command to console on UI thread
            Lines.Add(new ConsoleLine(DateTime.Now, $"> {cmd}", false));
            Input = string.Empty;

            try
            {
                var args = SplitShellLike(cmd);

                //Execute directly
                var (exitCode, stdout, stderr) = await _service.ExecAsync(ContainerId, args, tty: false);

                //back on UI thread so safe to add to Lines
                if (!string.IsNullOrEmpty(stdout))
                {
                    Lines.Add(new ConsoleLine(DateTime.Now, stdout.TrimEnd('\r', '\n'), false));
                }

                if (!string.IsNullOrEmpty(stderr))
                {
                    Lines.Add(new ConsoleLine(DateTime.Now, stderr.TrimEnd('\r', '\n'), true));
                }

                Lines.Add(new ConsoleLine(DateTime.Now, $"[exit] {exitCode}", false));
            }
            catch (Exception ex)
            {
                Lines.Add(new ConsoleLine(DateTime.Now, $"[exec-error] {ex.Message}", true));
            }
        }

        private bool CanStartLogs() => !IsLogsRunning && !string.IsNullOrWhiteSpace(ContainerId);

        [RelayCommand(CanExecute = nameof(CanStartLogs))]
        private async Task StartLogsAsync()
        {
            //Stop existing logs if it is currently running
            await StopLogsAsync();

            _cts = new();
            var ct = _cts.Token;

            var queue = new ConcurrentQueue<ConsoleLine>();

            //
            var uiTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));

            //store the UI update task so we can await it during cleanup
            _uiUpdateTask = Task.Run(async () =>
            {
                try
                {
                    while (await uiTimer.WaitForNextTickAsync(ct))
                    {
                        while (queue.TryDequeue(out var line))
                        {
                            AddLineToUI(line);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //flush any remaining items
                    while (queue.TryDequeue(out var line))
                    {
                        AddLineToUI(line);
                    }
                }
                finally
                {
                    uiTimer.Dispose();
                }
            }, ct);

            _logStreamTask = Task.Run(async () =>
            {
                try
                {
                    var reader = await _service.StreamLogsAsync(
                        ContainerId,
                        follow: true,
                        tail: Tail,
                        tty: Tty,
                        ct: ct);

                    await foreach (var (isErr, text) in reader.ReadAllAsync(ct))
                    {
                        if (text is null) continue;
                        queue.Enqueue(new ConsoleLine(DateTime.Now, text, isErr));
                    }

                    _cts?.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    queue.Enqueue(new ConsoleLine(DateTime.Now, "[logs] canceled", false));
                }
                catch (Exception ex)
                {
                    queue.Enqueue(new ConsoleLine(DateTime.Now, $"[logs-error] {ex.Message}", true));
                }
            }, ct);

            IsLogsRunning = true;
            UpdateCommandStates();
        }

        private bool CanStopLogs() => IsLogsRunning;

        [RelayCommand(CanExecute = nameof(CanStopLogs))]
        private async Task StopLogsAsync()
        {
            _cts?.Cancel();

            var toAwait = new List<Task>();
            if (_logStreamTask is not null) toAwait.Add(_logStreamTask);
            if (_uiUpdateTask is not null) toAwait.Add(_uiUpdateTask);

            try
            {
                if (toAwait.Count > 0)
                {
                    //await with a timeout to prevent hanging during shutdown
                    await Task.WhenAll(toAwait).WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
            catch (OperationCanceledException) {}
            catch (Exception) { /* shouldn't happen here... */ }
            finally
            {
                _logStreamTask = null;
                _uiUpdateTask = null;

                _cts?.Dispose();
                _cts = null;

                IsLogsRunning = false;
                UpdateCommandStates();
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
        /// Executes the specified user command asynchronously within the selected container.
        /// </summary>
        /// <remarks>This method sends the command to the selected container and captures the output,
        /// including standard output and error streams. The results are displayed in the UI. If no container is
        /// selected, the method logs a message indicating that no action was taken.</remarks>
        /// <param name="userCmd">The user command to execute. Can include the command and its arguments. If <paramref name="userCmd"/> is
        /// <see langword="null"/>, the method does not execute any command.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [RelayCommand]
        private async Task RunUserCommandAsync(UserCommand? userCmd)
        {
            // Check if the user command or selected container is null
            if (userCmd is null || SelectedContainer is null)
            {
                AddLineToUI(new ConsoleLine(DateTime.Now, "[user-cmd] No command selected or no container selected.", true));
                return;
            }
            // Get the command to execute and log it to the UI
            var cmd = userCmd.Command;
            AddLineToUI(new ConsoleLine(DateTime.Now, $"> {string.Join(' ', cmd)}", false));

            try
            {
                var (exitCode, stdout, stderr) = await _service.ExecAsync(SelectedContainer.Id, cmd, tty: false);
                if (!string.IsNullOrEmpty(stdout))
                {
                    AddLineToUI(new ConsoleLine(DateTime.Now, stdout.TrimEnd('\r', '\n'), false));
                }
                if (!string.IsNullOrEmpty(stderr))
                {
                    AddLineToUI(new ConsoleLine(DateTime.Now, stderr.TrimEnd('\r', '\n'), true));
                }
                AddLineToUI(new ConsoleLine(DateTime.Now, $"[exit] {exitCode}", false));
            }
            catch (Exception ex)
            {
                AddLineToUI(new ConsoleLine(DateTime.Now, $"[exec-error] {ex.Message}", true));
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

        private void AddLineToUI(ConsoleLine line)
        {
            if (_syncContext != null)
            {
                _syncContext.Post(state => Lines.Add((ConsoleLine)state!), line);
            }
            else
            {
                Lines.Add(line);
            }
        }

        private void UpdateCommandStates()
        {
            SendCommand.NotifyCanExecuteChanged();
            StartLogsCommand.NotifyCanExecuteChanged();
            StopLogsCommand.NotifyCanExecuteChanged();
        }

        partial void OnContainerIdChanged(string value)
        {
            UpdateCommandStates();
        }

        public override async ValueTask DisposeAsync()
        {
            await StopLogsAsync();
            await base.DisposeAsync();
        }
    }
}
