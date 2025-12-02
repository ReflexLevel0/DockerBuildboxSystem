using System;
using System.Collections.ObjectModel;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Interface based on region in standalone application for filesync.
    /// </summary>
    public interface IFileSyncService : IDisposable
    {
        ObservableCollection<string> Changes { get; }
        void StartWatching(string path, string containerId, string containerRootPath = "/data/");
        void StopWatching();
        void UpdateIgnorePatterns(string patterns);
    }
}
