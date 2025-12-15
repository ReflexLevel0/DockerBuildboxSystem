using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
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
                    await foreach (var (isErr, line) in _logRunner.RunAsync(_service, ContainerId).ConfigureAwait(false))
                    {
                        _logger.Log(line, isErr, false);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWithNewline("[logs] canceled", false, false);
                }
                catch (Exception ex)
                {
                    _logger.LogWithNewline($"[logs-error] {ex.Message}", true, true);
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
        public async Task StopLogsAsync()
        {
            await _logRunner.StopAsync();
        }
        public override async ValueTask DisposeAsync()
        {
            await StopLogsAsync();
            await base.DisposeAsync();
        }
    }
}
