using Docker.DotNet;
using Docker.DotNet.Models;
using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            DisposeAsync().AsTask().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Constructor for DockerService.
        /// </summary>
        /// <param name="endpoint">The docker endpoint URI</param>
        /// <param name="timeout">Optional timeout for Docker requests</param>
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
                        tty,
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


        public async Task<(long ExitCode, string StdOut, string StdErr)> ExecAsync(
            string containerId,
            IReadOnlyList<string> cmd,
            bool tty = false,
            CancellationToken ct = default)
        {
            var create = await _client.Exec.ExecCreateContainerAsync(containerId,
                new ContainerExecCreateParameters
                {
                    Cmd = cmd.ToList(),
                    AttachStdout = true,
                    AttachStderr = true,
                    AttachStdin = false,
                    Tty = tty
                }, ct);

            //Start and attach in one go... returns MultiplexedStream when TTY==false.
            using var attach = await _client.Exec.StartAndAttachContainerExecAsync(create.ID, tty, ct);

            //Collect output
            using var outMs = new MemoryStream();
            using var errMs = new MemoryStream();

            if (tty)
            {
                //With TTY, everything is merged to a single stream.
                await attach.CopyOutputToAsync(Stream.Null, outMs, Stream.Null, ct);
            }
            else
            {
                await attach.CopyOutputToAsync(Stream.Null, outMs, errMs, ct);
            }

            //Wait until the exec finishes and get exit code
            var resp = await _client.Exec.InspectContainerExecAsync(create.ID, ct);

            var stdout = Encoding.UTF8.GetString(outMs.ToArray());
            var stderr = Encoding.UTF8.GetString(errMs.ToArray());
            return (resp.ExitCode, stdout, stderr);
        }

        public async Task<(ChannelReader<(bool IsStdErr, string Line)> Output, Task<long> ExitCodeTask)> StreamExecAsync(
            string containerId,
            IReadOnlyList<string> cmd,
            bool tty = false,
            CancellationToken ct = default)
        {
            var ch = Channel.CreateUnbounded<(bool, string)>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            //Create the exec instance
            var create = await _client.Exec.ExecCreateContainerAsync(containerId,
                new ContainerExecCreateParameters
                {
                    Cmd = cmd.ToList(),
                    AttachStdout = true,
                    AttachStderr = true,
                    AttachStdin = false,
                    Tty = tty
                }, ct);

            var execId = create.ID;

            //A task that streams the output and returns the exit code
            var exitCodeTask = Task.Run(async () =>
            {
                MultiplexedStream? stream = null;
                CancellationTokenRegistration? registration = default;

                var buffer = new byte[4096];
                var lineBufferStdOut = new StringBuilder();
                var lineBufferStdErr = new StringBuilder();

                try
                {
                    //Start and attach to the exec instance
                    stream = await _client.Exec.StartAndAttachContainerExecAsync(execId, tty, ct).ConfigureAwait(false);

                    //Register cancellation callback to dispose stream immediately
                    registration = ct.Register(() => stream?.Dispose());

                    while(!ct.IsCancellationRequested)
                    {
                        //read from the multiplexed stream
                        var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, ct);

                        if (result.Count == 0)
                            break; //end of stream

                        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        bool isStdErr = result.Target == MultiplexedStream.TargetStream.StandardError;
                        var lineBuffer = isStdErr ? lineBufferStdErr : lineBufferStdOut;

                        //split by lines and add each line
                        var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            //incomplete line so save for next read
                            if (i == lines.Length - 1 && !text.EndsWith('\n'))
                            {
                                lineBuffer.Append(lines[i]);
                                continue;
                            }

                            var completeLine = lineBuffer.ToString() + lines[i];
                            if (!string.IsNullOrEmpty(completeLine) || (completeLine == string.Empty && i < lines.Length - 1))
                            {
                                await ch.Writer.WriteAsync((isStdErr, completeLine.TrimEnd('\r')), CancellationToken.None);
                            }
                            lineBuffer.Clear();
                        }
                    }

                    //add any remaining buffered text
                    if (lineBufferStdOut.Length > 0)
                    {
                        await ch.Writer.WriteAsync((false, lineBufferStdOut.ToString().TrimEnd('\r')), CancellationToken.None);
                    }
                    if (lineBufferStdErr.Length > 0)
                    {
                        await ch.Writer.WriteAsync((true, lineBufferStdErr.ToString().TrimEnd('\r')), CancellationToken.None);
                    }

                    //wait for the exec to finish and get exit code
                    var resp = await _client.Exec.InspectContainerExecAsync(execId, ct);
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
                    ch.Writer.TryComplete(ex);
                    throw;
                }
                finally
                {
                    //unregister the cancellation callback
                    registration?.Dispose();

                    //ensure the stream is disposed
                    stream?.Dispose();
                    ch.Writer.TryComplete();
                }
            }, ct);

            return (ch.Reader, exitCodeTask);
        }
        #endregion


        #region Helpers
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
