using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Defines a service for synchronizing files between a local directory and a containerized environment, providing
    /// change tracking and control over synchronization operations.
    /// </summary>
    public interface IFileSyncService : IDisposable
    {
        ObservableCollection<string> Changes { get; }

        /// <summary>
        /// Event that gets invoked when force sync starts
        /// </summary>
        event EventHandler? ForceSyncStarted;

        /// <summary>
        /// Event that gets invoked when force sync stopped
        /// </summary>
        event EventHandler? ForceSyncStopped;

        void Configure(string path, string containerId, string containerRootPath = "/data/");

        /// <summary>
        /// Starts watching the host directory for changes
        /// </summary>
        /// <param name="path">Path to the host directory</param>
        /// <param name="containerId">Container to which the directory is copied to</param>
        /// <param name="containerRootPath">Path in the containers to which the directory is copied to</param>
        /// <returns></returns>
        Task StartWatchingAsync(string path, string containerId, string containerRootPath = "/data/");
        
        /// <summary>
        /// Stops watching the host directory for changes
        /// </summary>
        void StopWatching();

        /// <summary>
        /// Temporarily pauses watching the host directory for changes
        /// </summary>
        void PauseWatching();

        /// <summary>
        /// Resumes watching the host directory for changes
        /// </summary>
        void ResumeWatching();

        /// <summary>
        /// Deletes directory in the container and copies the whole directory from the host to the container
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task ForceSyncAsync(CancellationToken ct = default);

        /// <summary>
        /// Copies the build directory from the container to the host
        /// </summary>
        /// <param name="hostPath">The host directory path to copy to</param>
        /// <param name="containerId">The container ID to copy from</param>
        /// <param name="containerPath">The container path to copy from</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task ForceSyncFromContainerAsync(string hostPath, string containerId, string containerPath, CancellationToken ct = default);

        /// <summary>
        /// Cleans the target directory in the container, excluding specified paths
        /// </summary>
        /// <param name="excludedPaths">Paths in the directory which will not be deleted</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task CleanDirectoryAsync(IEnumerable<string>? excludedPaths, CancellationToken ct = default);

        /// <summary>
        /// Loads ignore patterns from the .syncIgnore file
        /// </summary>
        /// <returns></returns>
        Task LoadIgnorePatternsAsync();
    }
}

