using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public sealed record ConsoleLine(DateTime Timestamp, string Text, bool IsError);

    public sealed partial class ContainerConsoleViewModel : ViewModelBase, IAsyncDisposable
    {
        private readonly IContainerService _service;
        private CancellationTokenSource? _cts;
        private Task? _logStreamTask;
        private readonly SynchronizationContext? _syncContext;

        public ObservableCollection<ConsoleLine> Lines { get; } = new();
        public ObservableCollection<ContainerInfo> Containers { get; } = new();

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

        public ContainerConsoleViewModel(IContainerService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _syncContext = SynchronizationContext.Current;
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
            var uiTimer = new System.Threading.Timer(_ =>
            {
                int n = 0;
                while (n < 200 && queue.TryDequeue(out var line))
                {
                    AddLineToUI(line);
                    n++;
                }
            }, null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));

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
                }
                catch (OperationCanceledException)
                {
                    queue.Enqueue(new ConsoleLine(DateTime.Now, "[logs] canceled", false));
                }
                catch (Exception ex)
                {
                    queue.Enqueue(new ConsoleLine(DateTime.Now, $"[logs-error] {ex.Message}", true));
                }
                finally
                {
                    //stop the timer
                    uiTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    uiTimer.Dispose();
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
            _cts?.Dispose();
            _cts = null;

            if (_logStreamTask is not null)
            {
                try { await _logStreamTask; } catch { }
                _logStreamTask = null;
            }

            IsLogsRunning = false;
            UpdateCommandStates();
        }


        [RelayCommand]
        private Task ClearAsync()
        {
            Lines.Clear();
            return Task.CompletedTask;
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

        public async ValueTask DisposeAsync()
        {
            await StopLogsAsync();
        }
    }
}
