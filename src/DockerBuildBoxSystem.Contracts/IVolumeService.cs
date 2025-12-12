using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    public interface IVolumeService : IAsyncDisposable
    {
        /// <summary>
        /// Gets the shared container volume
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <param name="createIfNotExists">If true, creates a new shared container</param>
        /// <returns>Shared volume</returns>
        Task<VolumeResponse?> GetSharedVolumeAsync(CancellationToken ct, bool createIfNotExists = false);

        /// <summary>
        /// Creates a shared container volume
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Created volume</returns>
        Task<VolumeResponse?> CreateSharedVolumeAsync(CancellationToken ct);
    }
}
