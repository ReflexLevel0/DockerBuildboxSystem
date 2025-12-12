using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public sealed partial class ContainerConsoleViewModel
    {
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
        public async Task OnSelectedContainerChangedAsync(ContainerInfo? value)
        {
            if (IsLoadingContainers && value is null) return;

            if (value?.Id == _previousContainerId && value?.Id != null) return;

            Interlocked.Increment(ref _switchingCount);
            IsSwitching = true;

            //cancel any pending start operations from a previous selection
            try
            {
                _switchCts?.Cancel();
            }
            catch (ObjectDisposedException) { /* ignoring... */ }

            _switchCts?.Dispose();
            _switchCts = new CancellationTokenSource();
            var ct = _switchCts.Token;

            try
            {
                await _containerSwitchLock.WaitAsync(CancellationToken.None);

                if (ct.IsCancellationRequested) return;

                var newContainer = value;
                //fallback to current if previous not tracked yet
                var oldId = _previousContainerId ?? ContainerId;

                //stop any running operations from previous container
                await StopLogsAsync();
                await StopExecAsync();
                UIHandler.DiscardPending();

                if (!string.IsNullOrWhiteSpace(oldId) && oldId != newContainer?.Id)
                {
                    var prev = Containers.FirstOrDefault(c => c.Id == oldId);
                    if (prev?.IsRunning == true)
                    {
                        await StopContainerByIdAsync(oldId);
                    }
                }

                if (ct.IsCancellationRequested) return;

                if (newContainer != null)
                {
                    ContainerId = newContainer.Id;
                    PostLogMessage($"[info] Selected container: {newContainer.Names.FirstOrDefault() ?? newContainer.Id}", false);

                    //auto start logs if enabled
                    if (AutoStartLogs && !string.IsNullOrWhiteSpace(ContainerId))
                        _ = StartLogs();
                }
                else
                {
                    ContainerId = string.Empty;
                }

                _previousContainerId = ContainerId;

                if (newContainer != null && !newContainer.IsRunning)
                {
                    await StartContainerInternalAsync(ct);
                }
            }
            catch (Exception ex)
            {
                PostLogMessage($"[selection-error] {ex.Message}", true);
            }
            finally
            {
                _containerSwitchLock.Release();

                if (Interlocked.Decrement(ref _switchingCount) == 0)
                {
                    IsSwitching = false;
                }
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
            await _containerSwitchLock.WaitAsync();
            try
            {
                await StartContainerInternalAsync(CancellationToken.None);
            }
            finally
            {
                _containerSwitchLock.Release();
            }
        }

        private async Task StartContainerInternalAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(ContainerId)) return;

            if (ct.IsCancellationRequested) return;

            try
            {
                PostLogMessage($"[info] Starting container: {ContainerId}", false);

                var status = await _service.StartAsync(ContainerId, ct);

                if (ct.IsCancellationRequested)
                {
                    PostLogMessage($"[info] Startup cancelled for: {ContainerId}", false);
                    return;
                }

                if (status)
                {
                    PostLogMessage($"[info] Started container: {ContainerId}", false);
                }
                else
                {
                    PostLogMessage($"[start-container] Container did not start: {ContainerId}", true);
                }
            }
            catch (OperationCanceledException)
            {
                PostLogMessage($"[info] Operation cancelled: {ContainerId}", false);
            }
            catch (Exception ex)
            {
                PostLogMessage($"[start-container-error] {ex.Message}", true);
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    _ = RefreshContainersCommand.ExecuteAsync(null);
                }
            }
        }
        private bool CanStopContainer() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == true);

        /// <summary>
        /// Stops a running container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopContainer))]
        private async Task StopContainerAsync() => await StopContainerByIdAsync(ContainerId);

        private bool CanRestartContainer() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == true);

        /// <summary>
        /// Stops a container by id (used when auto-stopping previous selection).
        /// </summary>
        private async Task StopContainerByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            try
            {
                var prev = Containers.FirstOrDefault(c => c.Id == id);
                var nameOrId = prev?.Names.FirstOrDefault() ?? id;
                PostLogMessage($"[info] Stopping container: {nameOrId}", false);
                await _service.StopAsync(id, timeout: TimeSpan.FromSeconds(10));
                PostLogMessage($"[info] Stopped container: {nameOrId}", false);
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

        /// <summary>
        /// Open the selected container in a Windows command prompt.
        /// </summary>
        /// <remarks>This command opens a new command prompt window with the specified container's shell.
        /// The container must be running for this command to succeed. It syncs with the currently selected container.
        /// </remarks>
        /// <returns>the task representing the asynchronous operation.</returns>
        [RelayCommand(CanExecute = nameof(IsRunning))]
        private async Task OpenContainerInCmd()
        {
            if (string.IsNullOrWhiteSpace(SelectedContainer?.Id)) return;
            try
            {
                PostLogMessage($"[info] Opening container in windows cmd: {SelectedContainer.Id}", false);
                _externalProcessService.StartProcess("cmd.exe", $"/K docker exec -it {SelectedContainer.Id} bash");
            }
            catch (Exception ex)
            {
                PostLogMessage($"[open-shell-error] {ex.Message}", true);
            }
        }
    }
}
