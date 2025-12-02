using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Interface based on region in standalone application for filesync.
    /// Changed the method signatures to both return the success status and the error message, not just string.
    /// </summary>
    public interface IContainerFileTransferService
    {
        Task<(bool Success, string Error)> CopyToContainerAsync(string containerId, string hostPath, string containerPath);
        Task<(bool Success, string Error)> DeleteInContainerAsync(string containerId, string containerPath);
        Task<(bool Success, string Error)> RenameInContainerAsync(string containerId, string oldPath, string newPath);
    }
}
