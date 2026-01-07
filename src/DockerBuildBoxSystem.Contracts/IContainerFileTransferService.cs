using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Interface based on region in standalone application for filesync.
    /// Changed the method signatures to both return the success status and the error message, not just string.
    /// </summary>
    public interface IContainerFileTransferService
    {
        Task<(bool Success, string Error)> CopyToContainerAsync(string containerId, string hostPath, string containerPath, CancellationToken ct = default);
        Task<(bool Success, string Error)> CopyFromContainerAsync(string containerId, string containerPath, string hostPath, CancellationToken ct = default);
        Task<(bool Success, string Error)> DeleteInContainerAsync(string containerId, string containerPath, CancellationToken ct = default);
        Task<(bool Success, string Error)> EmptyDirectoryInContainerAsync(string containerId, string containerPath, IEnumerable<string>? excludedPaths = null, CancellationToken ct = default);
        Task<(bool Success, string Error)> RenameInContainerAsync(string containerId, string oldPath, string newPath, CancellationToken ct = default);
        Task<(bool Success, string Error)> CopyDirectoryToContainerAsync(string containerId, string hostPath, string containerPath, CancellationToken ct = default);
        Task<(bool Success, string Error)> CopyDirectoryFromContainerAsync(string containerId, string containerPath, string hostPath, CancellationToken ct = default);
        Task<(bool Success, string Error)> CreateDirectoryInContainerAsync(string containerId, string containerPath, CancellationToken ct = default);
    }
}
