using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Represents a image and its essential information.
    /// </summary>
    public sealed class ImageInfo
    {
        /// <summary>
        /// The image ID.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// The repository tags (e.g., "ubuntu:latest").
        /// </summary>
        public required IReadOnlyList<string> RepoTags { get; init; }

        /// <summary>
        /// The date and time the image was created.
        /// </summary>
        public DateTime Created { get; init; }

        /// <summary>
        /// The size of the image in bytes.
        /// </summary>
        public long Size { get; init; }

        /// <summary>
        /// The virtual size of the image in bytes.
        /// </summary>
        public long VirtualSize { get; init; }

        /// <summary>
        /// The labels associated with the image.
        /// </summary>
        public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Defines an abstraction for interacting with images.
    /// </summary>
    public interface IImageService : IAsyncDisposable
    {
        /// <summary>
        /// Lists images existing on the host.
        /// </summary>
        /// <param name="all">If true, includes intermediate images</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Returns a list of <see cref="ImageInfo"/> objects.</returns>
        Task<IList<ImageInfo>> ListImagesAsync(bool all = false, CancellationToken ct = default);

        /// <summary>
        /// Retrieves detailed information about an image with a specified ID.
        /// </summary>
        /// <param name="imageId">The unique identifier of the image to inspect.</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>A <see cref="ImageInfo"/> object containing detailed information about the specified image.</returns>
        Task<ImageInfo?> InspectImageAsync(string imageId, CancellationToken ct = default);

        /// <summary>
        /// Removes an image from the Docker host.
        /// </summary>
        /// <param name="imageId">The id or name of the image to remove.</param>
        /// <param name="force">If true, forcibly removes the image.</param>
        /// <param name="prune">If true, prune untagged parents.</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns></returns>
        Task RemoveImageAsync(string imageId, bool force = false, bool prune = false, CancellationToken ct = default);

        /// <summary>
        /// Pulls an image from a registry.
        /// </summary>
        /// <param name="imageName">The name of the image to pull.</param>
        /// <param name="tag">The tag of the image to pull.</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns></returns>
        Task PullImageAsync(string imageName, string tag = "latest", CancellationToken ct = default);
    }
}
