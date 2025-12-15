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
        public async Task<(bool Success, string Error)> EmptyDirectoryInContainerAsync(string containerId, string containerPath, IEnumerable<string>? excludedPaths = null)
        {
            try
            {
                var excludes = new List<string>();
                if (excludedPaths != null)
                {
                    foreach (var exclude in excludedPaths)
                    {
                        //ensue we scape single quotes in filename
                        string safeExclude = exclude.Replace("'", "'\\''");
                        excludes.Add($"! -name '{safeExclude}'");
                    }
                }
                
                string excludeStr = string.Join(" ", excludes);
                string targetPath = containerPath.TrimEnd('/');
                if (string.IsNullOrEmpty(targetPath)) targetPath = "/";

                //The -mindepth 1 and -maxdepth 1 arguments ensures we only look at immediate children
                string cmd = $"find \"{targetPath}\" -mindepth 1 -maxdepth 1 {excludeStr} -exec rm -rf {{}} +";
                
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
