using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    public interface IContainerFileTransferService
    {
        /// <summary>
        /// Copies a file from the host to the container
        /// </summary>
        /// <param name="containerId">Container to which the file is copied to</param>
        /// <param name="hostPath">Path on the host where the file is located</param>
        /// <param name="containerPath">Path in the container where the file will be copied to</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<(bool Success, string Error)> CopyToContainerAsync(string containerId, string hostPath, string containerPath, CancellationToken ct = default);

        /// <summary>
        /// Copies a file from the container to the host
        /// </summary>
        /// <param name="containerId">Container to which the file is copied to</param>
        /// <param name="containerPath">Path in the container where the file is located</param>
        /// <param name="hostPath">Path on the host where the file will be copied to</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<(bool Success, string Error)> CopyFromContainerAsync(string containerId, string containerPath, string hostPath, CancellationToken ct = default);
        
        /// <summary>
        /// Deletes a file/directory from the container
        /// </summary>
        /// <param name="containerId">Container in which the file/directory is being deleted</param>
        /// <param name="containerPath">Path to the file/directory that is to be deleted</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<(bool Success, string Error)> DeleteInContainerAsync(string containerId, string containerPath, CancellationToken ct = default);
        
        /// <summary>
        /// Deletes all contents from the directory except for excluded paths
        /// </summary>
        /// <param name="containerId">Container in which the directory is located</param>
        /// <param name="containerPath">Path to the directory whose content is to be deleted</param>
        /// <param name="excludedPaths">Excluded paths, will not be deleted</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<(bool Success, string Error)> EmptyDirectoryInContainerAsync(string containerId, string containerPath, IEnumerable<string>? excludedPaths = null, CancellationToken ct = default);
        
        /// <summary>
        /// Renames or moves the file/directory in the container
        /// </summary>
        /// <param name="containerId">Container in which the file/directory is located at</param>
        /// <param name="oldPath">Old file/directory path</param>
        /// <param name="newPath">New file/directory path</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<(bool Success, string Error)> RenameInContainerAsync(string containerId, string oldPath, string newPath, CancellationToken ct = default);

        /// <summary>
        /// Copies a directory from the host to the container
        /// </summary>
        /// <param name="containerId">Container to which the directory is being copied to</param>
        /// <param name="hostPath">Path to the directory (on the host) that is being copied</param>
        /// <param name="containerPath">Path to where the directory will be copied to (in the container)</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<(bool Success, string Error)> CopyDirectoryToContainerAsync(string containerId, string hostPath, string containerPath, CancellationToken ct = default);
        
        /// <summary>
        /// Copies a directory from the container to the host
        /// </summary>
        /// <param name="containerId">Container in which the directory is located in</param>
        /// <param name="containerPath">Path in the container to the directory that will be copied</param>
        /// <param name="hostPath">Path on the host where the container will be copied to</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<(bool Success, string Error)> CopyDirectoryFromContainerAsync(string containerId, string containerPath, string hostPath, CancellationToken ct = default);
        
        /// <summary>
        /// Creates a directory in the container
        /// </summary>
        /// <param name="containerId">Container in which the directory is being created</param>
        /// <param name="containerPath">Path to the directory that will be created</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<(bool Success, string Error)> CreateDirectoryInContainerAsync(string containerId, string containerPath, CancellationToken ct = default);
    }
}
