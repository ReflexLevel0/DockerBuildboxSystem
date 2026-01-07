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
        void Configure(string path, string containerId, string containerRootPath = "/data/");
        Task StartWatchingAsync(string path, string containerId, string containerRootPath = "/data/");
        void StopWatching();
        void PauseWatching();
        void ResumeWatching();
        Task ForceSyncAsync(CancellationToken ct = default);
        Task ForceSyncFromContainerAsync(CancellationToken ct = default);
        Task CleanDirectoryAsync(IEnumerable<string>? excludedPaths, CancellationToken ct = default);
        void UpdateIgnorePatterns(string patterns);
        Task LoadIgnorePatternsAsync();
    }
}

