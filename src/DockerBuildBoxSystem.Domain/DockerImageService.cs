using Docker.DotNet;
using Docker.DotNet.Models;
using DockerBuildBoxSystem.Contracts;

namespace DockerBuildBoxSystem.Domain
{
    /// <summary>
    /// Service for image operations using Docker.
    /// </summary>
    public sealed class DockerImageService : DockerServiceBase, IImageService
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DockerImageService"/> class.
        /// </summary>
        /// <param name="endpoint">The docker endpoint URI. If null, a platform default is used retrieved from <see cref="GetDefaultDockerUri"/>.</param>
        /// <param name="timeout">Optional timeout for Docker requests. Defaults to 100 seconds.</param>
        public DockerImageService(string? endpoint = null, TimeSpan? timeout = null)
            : base(endpoint, timeout)
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DockerImageService"/> class with an existing client.
        /// </summary>
        /// <param name="client">An existing Docker client instance.</param>
        public DockerImageService(IDockerClient client)
            : base(client)
        {
        }

        /// <inheritdoc/>
        public async Task<ImageInfo?> InspectImageAsync(string imageId, CancellationToken ct = default)
        {
            try
            {
                var inspect = await Client.Images.InspectImageAsync(imageId, ct);
                return new ImageInfo
                {
                    Id = inspect.ID,
                    RepoTags = inspect.RepoTags is null ? Array.Empty<string>() : inspect.RepoTags.ToArray(),
                    Created = inspect.Created,
                    Size = inspect.Size,
                    VirtualSize = inspect.VirtualSize,
                    Labels = inspect.Config?.Labels is null ? new Dictionary<string, string>() : new Dictionary<string, string>(inspect.Config.Labels)
                };
            }
            catch (DockerImageNotFoundException)
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<IList<ImageInfo>> ListImagesAsync(bool all = false, CancellationToken ct = default)
        {
            var images = await Client.Images.ListImagesAsync(new ImagesListParameters { All = all }, ct);
            return images.Select(img => new ImageInfo
            {
                Id = img.ID,
                RepoTags = img.RepoTags is null ? Array.Empty<string>() : img.RepoTags.ToArray(),
                Created = img.Created,
                Size = img.Size,
                VirtualSize = img.VirtualSize,
                Labels = img.Labels is null ? new Dictionary<string, string>() : new Dictionary<string, string>(img.Labels)
            }).ToList();
        }

        /// <inheritdoc/>
        public async Task PullImageAsync(string imageName, string tag = "latest", CancellationToken ct = default)
        {
            await Client.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = imageName,
                    Tag = tag
                },
                null,
                new Progress<JSONMessage>(),
                ct);
        }

        /// <inheritdoc/>
        public async Task RemoveImageAsync(string imageId, bool force = false, bool prune = false, CancellationToken ct = default)
        {
            await Client.Images.DeleteImageAsync(imageId, new ImageDeleteParameters { Force = force, NoPrune = prune }, ct);
        }
    }
}
