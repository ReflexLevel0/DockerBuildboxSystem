using System;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Functionality for application settings, since the source folder path and sync out folder path need 
    /// to be shared between viewmodels.
    /// The previuous PersistSourcePathAsync and PersistSyncOutPathAsync methods had similar logic, 
    /// so this class just centralizes and also solves the issue with sharing settings between viewmodels.
    /// </summary>
    public interface ISettingsService
    {
        string SourceFolderPath { get; set; }
        string SyncOutFolderPath { get; set; }
        
        event EventHandler<string> SourcePathChanged;
        event EventHandler<string> SyncOutPathChanged;

        /// <summary>
        /// Asynchronously loads application settings from in-memory configuration and, if available, from a JSON
        /// configuration file on disk.
        /// </summary>
        Task LoadSettingsAsync();

        /// <summary>
        /// Asynchronously saves the current application settings to a JSON configuration file.
        /// </summary>
        Task SaveSettingsAsync();
    }
}
