using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public class ContainerFileTransferService : IContainerFileTransferService
    {
        private readonly IContainerService _containerService;

        public ContainerFileTransferService(IContainerService containerService)
        {
            _containerService = containerService;
        }

        public async Task<(bool Success, string Error)> CopyToContainerAsync(string containerId, string hostPath, string containerPath)
        {
            try
            {
                await _containerService.CopyFileToContainerAsync(containerId, hostPath, containerPath);
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> DeleteInContainerAsync(string containerId, string containerPath)
        {
            try
            {
                var (exitCode, output, error) = await _containerService.ExecAsync(containerId, new[] { "rm", "-rf", containerPath });
                if (exitCode != 0)
                    return (false, $"ERROR (ExitCode {exitCode}): {error}");
                return (true, output);
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }

        //maybe to dangerous?
        public async Task<(bool Success, string Error)> EmptyDirectoryInContainerAsync(string containerId, string containerPath)
        {
            try
            {
                string cmd = $"rm -rf \"{containerPath.TrimEnd('/')}\"/*";
                var (exitCode, output, error) = await _containerService.ExecAsync(containerId, new[] { "sh", "-c", cmd });
                
                if (exitCode != 0)
                    return (false, $"ERROR (ExitCode {exitCode}): {error}");
                return (true, output);
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> RenameInContainerAsync(string containerId, string oldPath, string newPath)
        {
            try
            {
                var (exitCode, output, error) = await _containerService.ExecAsync(containerId, new[] { "mv", oldPath, newPath });
                if (exitCode != 0)
                    return (false, $"ERROR (ExitCode {exitCode}): {error}");
                return (true, output);
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> CopyDirectoryToContainerAsync(string containerId, string hostPath, string containerPath)
        {
            try
            {
                await _containerService.CopyDirectoryToContainerAsync(containerId, hostPath, containerPath);
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, "ERROR: " + ex.Message);
            }
        }
    }
}
