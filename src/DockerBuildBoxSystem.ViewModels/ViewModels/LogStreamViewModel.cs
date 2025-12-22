using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public partial class LogStreamViewModel : ViewModelBase
    {
        private readonly ILogRunner _logRunner;
        private readonly IContainerService _service;
        private readonly IViewModelLogger _logger;
        private DateTime _lastLogTimeUtc;
        private bool _readySent;
        private CancellationTokenSource? _readyCts;
        private readonly TimeSpan _inactivityWindow = TimeSpan.FromSeconds(2);
        public string ContainerId
        {
            get => SelectedContainer?.Id ?? string.Empty;
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartLogsCommand))]
        private ContainerInfo? _selectedContainer;

        [ObservableProperty]
        private bool _autoStartLogs = true;

        public bool IsLogsRunning => _logRunner.IsRunning;
        public LogStreamViewModel(ILogRunner logRunner, IContainerService service, IViewModelLogger logger)
        {
            _logRunner = logRunner ?? throw new ArgumentNullException(nameof(logRunner));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logRunner.RunningChanged += (_, __) =>
            {
                SetOnUiThread(() =>
                {
                    OnPropertyChanged(nameof(IsLogsRunning));
                    StartLogsCommand.NotifyCanExecuteChanged();
                    StopLogsCommand.NotifyCanExecuteChanged();
                });
            };
        }
        /// <summary>
        /// Determines whether log streaming can be started.
        /// </summary>
        private bool CanStartLogs() => !IsLogsRunning && !string.IsNullOrWhiteSpace(ContainerId);
        
        /// <summary>
        /// Starts streaming container logs, following container TTY settings.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartLogs))]
        public Task StartLogsAsync()
        {
            return Task.Run(async () =>
            {
                try
                {
                    _readySent = false;
                    _lastLogTimeUtc = DateTime.UtcNow;
                    _readyCts?.Cancel();
                    _readyCts = new CancellationTokenSource();
                    var readyToken = _readyCts.Token;

                    // Background monitor: when logs are inactive for a window, broadcast ready
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            while (!readyToken.IsCancellationRequested)
                            {
                                await Task.Delay(500, readyToken);
                                var idleFor = DateTime.UtcNow - _lastLogTimeUtc;
                                if (!_readySent && idleFor >= _inactivityWindow && SelectedContainer is not null && SelectedContainer.IsRunning)
                                {
                                        try
                                        {
                                            WeakReferenceMessenger.Default.Send(new ContainerReadyMessage(SelectedContainer));
                                        }
                                    catch { }
                                    finally
                                    {
                                        _readySent = true;
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                    });

                    await foreach (var (isErr, line) in _logRunner.RunAsync(_service, ContainerId).ConfigureAwait(false))
                    {
                        _lastLogTimeUtc = DateTime.UtcNow;
                        _logger.LogWithNewline(line, isErr, false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Suppress cancellation message to avoid console noise
                }
                catch (Exception ex)
                {
                    _logger.LogWithNewline($"[logs-error] {ex.Message}", true, true);
                }

            });
        }

        partial void OnSelectedContainerChanged(ContainerInfo? oldValue, ContainerInfo? newValue)
        {
            var sameContainer = oldValue?.Id == newValue?.Id;

            //if we didn't actually switch containers and logs are already running, do nothing.
            if (sameContainer && _logRunner.IsRunning)
                return;

            //stop logs for the previous container
            if (_logRunner.IsRunning && StopLogsCommand.CanExecute(null))
                StopLogsCommand.Execute(null);

            //auto-start logs for the new container
            if (newValue is null || !AutoStartLogs)
                return;

            if (newValue.IsRunning && StartLogsCommand.CanExecute(null))
                StartLogsCommand.Execute(null);
        }

        /// <summary>
        /// Determines whether log streaming can be stopped.
        /// </summary>
        private bool CanStopLogs() => IsLogsRunning;

        /// <summary>
        /// Stops the current log streaming task, if it is running.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopLogs))]
        public async Task StopLogsAsync()
        {
            try { _readyCts?.Cancel(); } catch { }
            await _logRunner.StopAsync();
        }
        public override async ValueTask DisposeAsync()
        {
            await StopLogsAsync();
            await base.DisposeAsync();
        }
    }
}
