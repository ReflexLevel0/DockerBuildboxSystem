using Docker.DotNet;
using Docker.DotNet.Models;
using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    /// <summary>
    /// Service for volume operations using Docker.
    /// </summary>
    public sealed class DockerVolumeService : DockerServiceBase, IVolumeService
    {
        private static readonly string _sharedVolumeName = "BuildBoxShared";

        /// <summary>
        /// Initializes a new instance of the <see cref="DockerVolumeService"/> class.
        /// </summary>
        /// <param name="endpoint">The docker endpoint URI. If null, a platform default is used.</param>
        /// <param name="timeout">Optional timeout for Docker requests. Defaults to 100 seconds.</param>
        public DockerVolumeService(string? endpoint = null, System.TimeSpan? timeout = null)
            : base(endpoint, timeout)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DockerVolumeService"/> class with an existing client.
        /// </summary>
        /// <param name="client">An existing Docker client instance.</param>
        public DockerVolumeService(DockerClient client)
            : base(client)
        {
        }

        public async Task<VolumeResponse?> GetSharedVolumeAsync(CancellationToken ct, bool createIfNotExists = false)
        {
            // Finding a volume named "BuildBoxShared"
            var volumes = (await Client.Volumes.ListAsync(ct)).Volumes;
            var sharedVolume = volumes
                .Where(v => string.CompareOrdinal(v.Name, _sharedVolumeName) == 0)
                .FirstOrDefault();

            // Returning the volume
            // (or creating and returning a new volume if shared volume doesn't exist)
            return sharedVolume == null && createIfNotExists ? await CreateSharedVolumeAsync(ct) : sharedVolume;
        }
        public async Task<VolumeResponse?> CreateSharedVolumeAsync(CancellationToken ct)
        {
            // Creates a volume named "BuildBoxShared"
            var volumeParams = new VolumesCreateParameters() { Name = _sharedVolumeName };
            return await Client.Volumes.CreateAsync(volumeParams, ct);
        }
    }
}
