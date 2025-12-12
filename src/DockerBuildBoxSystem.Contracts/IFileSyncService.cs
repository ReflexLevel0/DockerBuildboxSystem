using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Interface based on region in standalone application for filesync.
    /// </summary>
    public interface IFileSyncService : IDisposable
    {
        ObservableCollection<string> Changes { get; }
        void Configure(string path, string containerId, string containerRootPath = "/data/");
        void StartWatching(string path, string containerId, string containerRootPath = "/data/");
        void StopWatching();
        Task ForceSyncAsync();
        Task CleanDirectoryAsync(IEnumerable<string>? excludedPaths = null);
        void UpdateIgnorePatterns(string patterns);
    }
}

