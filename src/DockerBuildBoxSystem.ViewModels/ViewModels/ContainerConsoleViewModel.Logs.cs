using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public sealed partial class ContainerConsoleViewModel
    {
        /// <summary>
        /// Determines whether log streaming can be started.
        /// </summary>
        private bool CanStartLogs() => !IsLogsRunning && !string.IsNullOrWhiteSpace(ContainerId);

        /// <summary>
        /// Starts streaming container logs, following container TTY settings.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartLogs))]
        private Task StartLogs()
        {
            return Task.Run(async () =>
            {
                try
                {
                    await foreach (var (isErr, line) in _logRunner.RunAsync(_service, ContainerId).ConfigureAwait(false))
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
        private async Task StopLogsAsync()
        {
            await _logRunner.StopAsync();
        }
    }
}
