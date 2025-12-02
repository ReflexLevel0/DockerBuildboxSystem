using Docker.DotNet;
using Docker.DotNet.Models;
using DockerBuildBoxSystem.Contracts;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DockerBuildBoxSystem.Domain
{
    /// <summary>
    /// Class for interactions with the Docker Engine API using Docker.DotNet.
    /// </summary>
    public sealed class DockerService : IContainerService, IAsyncDisposable, IDisposable
    {
        #region Variables and Constructor
        private readonly DockerClient _client;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="DockerService"/> class.
        /// </summary>
        /// <param name="endpoint">The docker endpoint URI. If null, a platform default is used retrieved from <see cref="GetDefaultDockerUri"/>.</param>
        /// <param name="timeout">Optional timeout for Docker requests. Defaults to 100 seconds.</param>
        public DockerService(string? endpoint = null, TimeSpan? timeout = null)
        {
            _client = new DockerClientConfiguration(
                endpoint is not null ? new Uri(endpoint) : GetDefaultDockerUri(),
                new AnonymousCredentials(),
                default,
                timeout ?? TimeSpan.FromSeconds(100))
                .CreateClient();
        }

        #endregion

        #region Container Operations
        public async Task<bool> StartAsync(string containerId, CancellationToken ct = default) =>
            await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);

        public async Task StopAsync(string containerId, TimeSpan timeout, CancellationToken ct = default) =>
            await _client.Containers.StopContainerAsync(containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = (uint)timeout.TotalSeconds }, ct);


        public async Task RemoveAsync(string containerId, bool force = false, CancellationToken ct = default) =>
            await _client.Containers.RemoveContainerAsync(containerId,
                new ContainerRemoveParameters { Force = force }, ct);


        public async Task RestartAsync(string containerId, TimeSpan timeout, CancellationToken ct = default) =>
            await _client.Containers.RestartContainerAsync(containerId,
                new ContainerRestartParameters { WaitBeforeKillSeconds = (uint)timeout.TotalSeconds }, ct);


        public async Task KillContainer(string containerId, CancellationToken ct = default) =>
            await _client.Containers.KillContainerAsync(containerId, new ContainerKillParameters(), ct);

        public async Task<ContainerInfo> InspectAsync(string containerId, CancellationToken ct = default)
        {
            var inspect = await _client.Containers.InspectContainerAsync(containerId, ct);

            return new ContainerInfo
            {
                Id = inspect.ID,
                Names = string.IsNullOrEmpty(inspect.Name) ? Array.Empty<string>() : [inspect.Name],
                Status = inspect.State?.Status,
                Tty = inspect.Config?.Tty ?? false,
                LogDriver = inspect.HostConfig?.LogConfig?.Type
            };
        }

        public async Task<IList<ContainerInfo>> ListContainersAsync(
            bool all = false,
            string? nameFilter = null,
            CancellationToken ct = default)
        {
            var filters = nameFilter is null ? null : new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [nameFilter] = true }
            };

            var parameters = new ContainersListParameters
            {
                All = all,
                Filters = filters
            };

            var containers = await _client.Containers.ListContainersAsync(parameters, ct);

            //mapping the Docker.DotNet model to our own DTO
            return containers.Select(c => new ContainerInfo
            {
                Id = c.ID,
                Names = c.Names.AsReadOnly(),
                State = c.State,
                Status = c.Status,
                Image = c.Image
            }).ToList();
        }



        public async Task<ChannelReader<(bool IsStdErr, string Line)>> StreamLogsAsync(
            string containerId,
            bool follow = true,
            string tail = "all",
            bool tty = false,
            CancellationToken ct = default)
        {
            var ch = Channel.CreateUnbounded<(bool, string)>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            _ = Task.Run(async () =>
            {
                MultiplexedStream? stream = null;
                CancellationTokenRegistration? registration = default;

                var buffer = new byte[4096];
                var lineBufferStdOut = new StringBuilder();
                var lineBufferStdErr = new StringBuilder();

                try
                {
                    stream = await _client.Containers.GetContainerLogsAsync(
                        containerId,
                        new ContainerLogsParameters
                        {
                            ShowStdout = true,
                            ShowStderr = true,
                            Follow = follow,
                            Timestamps = false,
                            Tail = tail
                        },
                        ct).ConfigureAwait(false);

                    // Register cancellation callback to dispose stream immediately
                    // This ensures the ReadOutputAsync call is interrupted
                    registration = ct.Register(() => stream?.Dispose());

                    while (!ct.IsCancellationRequested)
                    {
                        //using ReadOutputAsync to read the multiplexed stream
                        var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, ct);

                        if (result.Count == 0)
                            break; //end of stream

                        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        bool isStdErr = result.Target == MultiplexedStream.TargetStream.StandardError;
                        var lineBuffer = isStdErr ? lineBufferStdErr : lineBufferStdOut;

                        //split by lines and emit each line
                        var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (i == lines.Length - 1 && !text.EndsWith('\n'))
                            {
                                //Incomplete line, save for next read
                                lineBuffer.Append(lines[i]);
                            }
                            else
                            {
                                var completeLine = lineBuffer.ToString() + lines[i];
                                if (!string.IsNullOrEmpty(completeLine) || (completeLine == string.Empty && i < lines.Length - 1))
                                {
                                    await ch.Writer.WriteAsync((isStdErr, completeLine.TrimEnd('\r')), CancellationToken.None);
                                }
                                lineBuffer.Clear();
                            }
                        }
                    }

                    //Emitting any remaining buffered text
                    if (lineBufferStdOut.Length > 0)
                    {
                        await ch.Writer.WriteAsync((false, lineBufferStdOut.ToString().TrimEnd('\r')), CancellationToken.None);
                    }
                    if (lineBufferStdErr.Length > 0)
                    {
                        await ch.Writer.WriteAsync((true, lineBufferStdErr.ToString().TrimEnd('\r')), CancellationToken.None);
                    }
                }
                catch (OperationCanceledException)
                {
                    //Expected when cancellation is requested
                }
                catch (ObjectDisposedException)
                {
                    //Expected when stream is disposed due to cancellation
                }
                catch (Exception ex)
                {
                    ch.Writer.TryComplete(ex);
                    return;
                }
                finally
                {
                    //unregister the cancellation callback
                    registration?.Dispose();
                    
                    //ensure the stream is disposed even if cancelled! This resolves the issue of the application keeps running even though window was closed! :D
                    stream?.Dispose();
                    ch.Writer.TryComplete();
                }

            }, ct);

            return ch.Reader;
        }


        public async Task<(ChannelReader<(bool IsStdErr, string Line)> Output, ChannelWriter<string> Input, Task<long> ExitCodeTask)> StreamExecAsync(
            string containerId,
            IReadOnlyList<string> cmd,
            bool tty = false,
            CancellationToken ct = default)
        {
            var outCh = Channel.CreateUnbounded<(bool, string)>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            var inCh = Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            //Create the exec instance
            var create = await _client.Exec.CreateContainerExecAsync(containerId,
                new ContainerExecCreateParameters
                {
                    Cmd = cmd.ToList(),
                    AttachStdout = true,
                    AttachStderr = true,
                    AttachStdin = true,
                    Tty = tty
                }, ct);

            var execId = create.ID;

            MultiplexedStream? stream = null;
            CancellationTokenRegistration? registration = default;

            //Start and attach to the exec instance
            stream = await _client.Exec.StartContainerExecAsync(execId, new ContainerExecStartParameters { Detach = false, Tty = tty }, ct);

            using var baseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var stdinCts = CancellationTokenSource.CreateLinkedTokenSource(baseCts.Token);
            using var stdoutCts = CancellationTokenSource.CreateLinkedTokenSource(baseCts.Token);
            var stdinToken = stdinCts.Token;
            var stdoutToken = stdoutCts.Token;

            //Register cancellation callback to dispose stream immediately
            registration = ct.Register(() => stream?.Dispose());

            var inputCodeTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var chunk in inCh.Reader.ReadAllAsync(stdinToken))
                    {
                        var data = EncodeTerminalInput(chunk);
                        await stream.WriteAsync(data, 0, data.Length, stdinToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { outCh.Writer.TryComplete(ex); }
                finally
                {
                    try { stream?.CloseWrite(); } catch { }
                }
            }, stdinToken);

            //A task that streams the output and returns the exit code
            var exitCodeTask = Task.Run(async () =>
            {
                var buffer = ArrayPool<byte>.Shared.Rent(8192);
                var lineBufferStdOut = new StringBuilder();
                var lineBufferStdErr = new StringBuilder();

                try
                {

                    while (!stdoutToken.IsCancellationRequested)
                    {
                        var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, stdoutToken);
                        if (result.Count == 0)
                            break; // EOF

                        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        if (tty)
                        {
                            // TTY merges streams and REPLs often don't end with '\n'
                            // => forward raw chunks immediately so prompts/banner show up
                            await outCh.Writer.WriteAsync((false, text), CancellationToken.None);
                            continue;
                        }

                        bool isStdErr = result.Target == MultiplexedStream.TargetStream.StandardError;
                        var lineBuffer = isStdErr ? lineBufferStdErr : lineBufferStdOut;

                        //split by lines and add each line
                        var lines = text.Split(['\n'], StringSplitOptions.None);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            //incomplete line so save for next read
                            if (i == lines.Length - 1 && !text.EndsWith('\n'))
                            {
                                lineBuffer.Append(lines[i]);
                                continue;
                            }

                            var completeLine = lineBuffer.ToString() + lines[i];
                            lineBuffer.Clear();
                            await outCh.Writer.WriteAsync((isStdErr, completeLine.TrimEnd('\r')), CancellationToken.None)
                                            .ConfigureAwait(false);
                        }
                    }

                    //add any remaining buffered text
                    if (!tty)
                    {
                        if (lineBufferStdOut.Length > 0)
                            await outCh.Writer.WriteAsync((false, lineBufferStdOut.ToString().TrimEnd('\r')), CancellationToken.None);
                        if (lineBufferStdErr.Length > 0)
                            await outCh.Writer.WriteAsync((true, lineBufferStdErr.ToString().TrimEnd('\r')), CancellationToken.None);
                    }


                    //wait for the exec to finish and get exit code
                    var resp = await _client.Exec.InspectContainerExecAsync(execId, stdoutToken).ConfigureAwait(false);
                    return resp.ExitCode;
                }
                catch (OperationCanceledException)
                {
                    //if cancellation is requested
                    throw;
                }
                catch (ObjectDisposedException)
                {
                    //if stream is disposed due to cancellation
                    throw new OperationCanceledException();
                }
                catch (Exception ex)
                {
                    outCh.Writer.TryComplete(ex);
                    throw;
                }
                finally
                {
                    //Stop stdin
                    try { stdinCts.Cancel(); } catch { }
                    try { inCh.Writer.TryComplete(); } catch { }

                    //Break any I/O and release resources
                    try { registration?.Dispose(); } catch { }
                    try { stream?.Dispose(); } catch { }

                    //Complete output channel
                    outCh.Writer.TryComplete();

                    try { await inputCodeTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                    catch { /* shutting it down */ }
                }
            }, stdoutToken);


            return (outCh.Reader, inCh.Writer, exitCodeTask);
        }

        public async Task<(long ExitCode, string Output, string Error)> ExecAsync(
            string containerId,
            IReadOnlyList<string> cmd,
            CancellationToken ct = default)
        {
            var create = await _client.Exec.CreateContainerExecAsync(containerId,
                new ContainerExecCreateParameters
                {
                    Cmd = cmd.ToList(),
                    AttachStdout = true,
                    AttachStderr = true,
                    Tty = false
                }, ct);

            var execId = create.ID;

            using var stream = await _client.Exec.StartContainerExecAsync(execId, new ContainerExecStartParameters { Detach = false, Tty = false }, ct);

            var output = await stream.ReadOutputToEndAsync(ct);

            var inspect = await _client.Exec.InspectContainerExecAsync(execId, ct);

            return (inspect.ExitCode, output.stdout, output.stderr);
        }

        public async Task CopyFileToContainerAsync(
            string containerId,
            string hostPath,
            string containerPath,
            CancellationToken ct = default)
        {
            if (!File.Exists(hostPath))
                throw new FileNotFoundException("File not found", hostPath);

            /*
                There are no "docker cp" equivelent in the docker.dotnet nuget package.
                However, the "docker cp" is actually just a wrapper that uses compression, see the implementation:
                    https://github.com/docker/cli/blob/master/cli/command/container/cp.go#L418
                
            */
            string fileName = Path.GetFileName(containerPath);
            string dirName = Path.GetDirectoryName(containerPath)?.Replace('\\', '/') ?? "/";

            // Ensure directory exists
            await ExecAsync(containerId, new[] { "mkdir", "-p", dirName }, ct);

            using var memoryStream = new MemoryStream();
            using (var tarWriter = new TarWriter(memoryStream, leaveOpen: true))
            {
                //create a tar entry from the host file, but rename it to the target filename
                await tarWriter.WriteEntryAsync(hostPath, fileName, ct);
            }

            memoryStream.Position = 0;

            await _client.Containers.ExtractArchiveToContainerAsync(containerId,
                new ContainerPathStatParameters { Path = dirName },
                memoryStream,
                ct);
        }
        #endregion


        #region Helpers

        /// <summary>
        /// Disposes the underlying Docker client
        /// </summary>
        /// <returns>A task that represents the dispose operation.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            //dispose synchronously
            _client?.Dispose();

            await Task.CompletedTask;

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Synchronously disposes resources by delegating to <see cref="DisposeAsync"/> and blocking until completion.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            DisposeAsync().AsTask().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }
        private static byte[] EncodeTerminalInput(string? value)
        {
            var normalized = value?.Replace("\r\n", "\n", StringComparison.Ordinal)
                               .Replace('\r', '\n') ?? string.Empty;

            if (!normalized.EndsWith('\n'))
            {
                normalized += '\n';
            }

            //interactive TTY sessions expect carriage returns to signal end of a line.
            var carriageReturnNormalized = normalized.Replace('\n', '\r');
            return Encoding.UTF8.GetBytes(carriageReturnNormalized);
        }
        /// <summary>
        /// Gets the default URI for connecting to the Docker engine based on the current operating system.
        /// </summary>
        /// <remarks>The returned URI is determined by the operating system at runtime.  Use this method
        /// to obtain the appropriate default Docker engine URI for the current environment.</remarks>
        /// <returns>A <see cref="Uri"/> representing the default Docker engine connection endpoint.  On Windows, this is
        /// <c>npipe://./pipe/docker_engine</c>.  On non-Windows platforms, this is <c>unix:///var/run/docker.sock</c>.</returns>
        private static Uri GetDefaultDockerUri()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new Uri("npipe://./pipe/docker_engine");
            return new Uri("unix:///var/run/docker.sock");
        }



        #endregion
    }
}
