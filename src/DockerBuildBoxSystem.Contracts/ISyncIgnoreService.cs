using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Defines methods for loading, saving, and opening the sync ignore configuration used to specify files or folders
    /// excluded from synchronization operations.
    /// </summary>
    /// <remarks>Implementations of this interface typically interact with a sync ignore file to manage
    /// exclusion rules. The interface provides asynchronous methods for reading and writing the configuration, as well
    /// as a method to open the configuration for manual editing.</remarks>
    public interface ISyncIgnoreService
    {
        string FilePath { get; }
        Task <List<string>> LoadSyncIgnoreAsync();
        Task SaveSyncIgnoreAsync(List<string> syncIgnore);
        void OpenSyncIgnore();
    }
}
