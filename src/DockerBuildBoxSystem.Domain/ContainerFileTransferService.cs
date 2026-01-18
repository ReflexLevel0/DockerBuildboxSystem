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

        public async Task<(bool Success, string Error)> CopyToContainerAsync(string containerId, string hostPath, string containerPath, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await _containerService.CopyFileToContainerAsync(containerId, hostPath, containerPath, ct);
                return (true, string.Empty);
            }
            catch (OperationCanceledException)
            {
                return (false, "Cancelled");
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> CopyFromContainerAsync(string containerId, string containerPath, string hostPath, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await _containerService.CopyFileFromContainerAsync(containerId, containerPath, hostPath, ct);
                return (true, string.Empty);
            }
            catch (OperationCanceledException)
            {
                return (false, "Cancelled");
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }


        public async Task<(bool Success, string Error)> DeleteInContainerAsync(string containerId, string containerPath, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var (exitCode, output, error) = await _containerService.ExecAsync(containerId, new[] { "rm", "-rf", containerPath }, ct);
                if (exitCode != 0)
                    return (false, $"ERROR (ExitCode {exitCode}): {error}");
                return (true, output);
            }
            catch (OperationCanceledException)
            {
                return (false, "Cancelled");
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }

        //maybe to dangerous?
        public async Task<(bool Success, string Error)> EmptyDirectoryInContainerAsync(string containerId, string containerPath, IEnumerable<string>? excludedPaths = null, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var excludes = new List<string>();
                if (excludedPaths != null)
                {
                    foreach (var exclude in excludedPaths)
                    {
                        // Skip empty or whitespace-only paths
                        if (string.IsNullOrWhiteSpace(exclude))
                            continue;

                        //ensue we scape single quotes in filename
                        string safeExclude = exclude.Replace("'", "'\\''");
                        excludes.Add($"! -name '{safeExclude}'");
                    }
                }
                
                string excludeStr = excludes.Count > 0 ? string.Join(" ", excludes) : "";
                string targetPath = containerPath.TrimEnd('/');
                if (string.IsNullOrEmpty(targetPath)) targetPath = "/";

                //The -mindepth 1 and -maxdepth 1 arguments ensures we only look at immediate children
                string cmd = $"find \"{targetPath}\" -mindepth 1 -maxdepth 1 {excludeStr} -exec rm -rf {{}} +";
                
                var (exitCode, output, error) = await _containerService.ExecAsync(containerId, new[] { "sh", "-c", cmd }, ct);
                
                if (exitCode != 0)
                    return (false, $"ERROR (ExitCode {exitCode}): {error}");
                return (true, output);
            }
            catch (OperationCanceledException)
            {
                return (false, "Cancelled");
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> RenameInContainerAsync(string containerId, string oldPath, string newPath, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var (exitCode, output, error) = await _containerService.ExecAsync(containerId, new[] { "mv", oldPath, newPath }, ct);
                if (exitCode != 0)
                    return (false, $"ERROR (ExitCode {exitCode}): {error}");
                return (true, output);
            }
            catch (OperationCanceledException)
            {
                return (false, "Cancelled");
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> CopyDirectoryToContainerAsync(string containerId, string hostPath, string containerPath, CancellationToken ct = default)
        {
            try
            {
                await _containerService.CopyDirectoryToContainerAsync(containerId, hostPath, containerPath, ct);
                return (true, string.Empty);
            }
            catch (OperationCanceledException)
            {
                return (false, "Cancelled");
            }
            catch (Exception ex)
            {
                return (false, "ERROR: " + ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> CopyDirectoryFromContainerAsync(string containerId, string containerPath, string hostPath, CancellationToken ct = default)
        {
            try
            {
                await _containerService.CopyDirectoryFromContainerAsync(containerId, containerPath, hostPath, ct);
                return (true, string.Empty);
            }
            catch (OperationCanceledException)
            {
                return (false, "Cancelled");
            }
            catch (Exception ex)
            {
                return (false, "ERROR: " + ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> CreateDirectoryInContainerAsync(string containerId, string containerPath, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var (exitCode, output, error) = await _containerService.ExecAsync(containerId, new[] { "mkdir", "-p", containerPath }, ct);
                if (exitCode != 0)
                    return (false, $"ERROR (ExitCode {exitCode}): {error}");
                return (true, output);
            }
            catch (OperationCanceledException)
            {
                return (false, "Cancelled");
            }
            catch (Exception ex)
            {
                return (false, "EXCEPTION: " + ex.Message);
            }
        }
    }
}
