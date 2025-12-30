using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using CommunityToolkit.Mvvm.Messaging;
using DockerBuildBoxSystem.ViewModels.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public partial class LogStreamViewModel : ViewModelBase, IRecipient<SelectedContainerChangedMessage>
    {
        private readonly ILogRunner _logRunner;
        private readonly IContainerService _service;
        private readonly IViewModelLogger _logger;
        public string ContainerId
        {
            get => SelectedContainer?.Id ?? string.Empty;
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartLogsCommand))]
        private ContainerInfo? _selectedContainer;

        [ObservableProperty]
        private bool _autoStartLogs = true;

        partial void OnAutoStartLogsChanged(bool value)
        {
            //notify other view models about auto-start logs setting change
            WeakReferenceMessenger.Default.Send(new AutoStartLogsChangedMessage(value));
        }

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

            // Register to receive messages
            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        /// <summary>
        /// Handles the SelectedContainerChangedMessage.
        /// </summary>
        public void Receive(SelectedContainerChangedMessage message)
        {
            SelectedContainer = message.Value;
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
                    await foreach (var (isErr, line) in _logRunner.RunAsync(_service, ContainerId).ConfigureAwait(false))
                    {
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
            await _logRunner.StopAsync();
        }
        public override async ValueTask DisposeAsync()
        {
            // Unregister message subscriptions
            WeakReferenceMessenger.Default.UnregisterAll(this);
            await StopLogsAsync();
            await base.DisposeAsync();
        }
    }
}
