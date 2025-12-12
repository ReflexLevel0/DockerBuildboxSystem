using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public sealed partial class ContainerConsoleViewModel
    {
        /// <summary>
        /// Determines whether sync can be started.
        /// </summary>
        private bool CanSync() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == true) && !IsSyncRunning && !IsCommandRunning;

        /// <summary>
        /// Starts the sync operation.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSync))]
        private async Task StartSyncAsync()
        {
            if (string.IsNullOrWhiteSpace(HostSyncPath))
            {
                PostLogMessage("[sync-error] Error: Host sync path is not set!", true);
                return;
            }

            if (!Directory.Exists(HostSyncPath))
            {
                PostLogMessage($"[sync-error] Error: Host directory does not exist: {HostSyncPath}", true);
                return;
            }

            IsSyncRunning = true;
            try
            {
                _fileSyncService.StartWatching(HostSyncPath, ContainerId, ContainerSyncPath);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                PostLogMessage($"[sync-error] {ex.Message}", true);
                IsSyncRunning = false;
            }
        }

        /// <summary>
        /// Starts the force sync operation (same constraints as startSync).
        /// This functiona should delete all sync in data from docker volume and resync everything.
        /// To be implemented once file sync functionality is in place.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSync))]
        private async Task StartForceSyncAsync()
        {
            IsSyncRunning = true;
            try
            {
                PostLogMessage("[force-sync] Starting force sync operation", false);
                
                if (string.IsNullOrWhiteSpace(HostSyncPath) || !Directory.Exists(HostSyncPath))
                {
                     PostLogMessage("[force-sync] Error: Host sync path is invalid.", true);
                     return;
                }
                
                _fileSyncService.Configure(HostSyncPath, ContainerId, ContainerSyncPath);
                
                await _fileSyncService.ForceSyncAsync();
                
                PostLogMessage("[force-sync] Completed force sync operation", false);
            }
            catch (Exception ex)
            {
                PostLogMessage($"[force-sync-error] {ex.Message}", true);
            }
            finally
            {
                IsSyncRunning = false;
            }
        }

        /// <summary>
        /// Determines whether sync operation can be stopped.
        /// </summary>
        private bool CanStopSync() => IsSyncRunning;

        /// <summary>
        /// Stops the current sync task, if it is running.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopSync))]
        private async Task StopSyncAsync()
        {
            _fileSyncService.StopWatching();
            IsSyncRunning = false;
            PostLogMessage("[sync] Stopped watching.", false);
            await Task.CompletedTask;
        }
    }
}
