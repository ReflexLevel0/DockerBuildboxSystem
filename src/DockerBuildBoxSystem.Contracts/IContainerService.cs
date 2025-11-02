using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{    
    /// <summary>
    /// Represents a container with essential information.
    /// </summary>
    public sealed class ContainerInfo
    {
        /// <summary>
        /// The container ID.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// The container names.
        /// </summary>
        public required IReadOnlyList<string> Names { get; init; }

        /// <summary>
        /// The container state (e.g., "running", "exited").
        /// </summary>
        public string? State { get; init; }

        /// <summary>
        /// The container status description.
        /// </summary>
        public string? Status { get; init; }

        /// <summary>
        /// The image name used by the container.
        /// </summary>
        public string? Image { get; init; }

        //whether the container was started with TTY (Config.Tty)
        public bool Tty { get; init; }

        //HostConfig.LogConfig.Type (e.g., "json-file", "none", "local")
        public string? LogDriver { get; init; }
    }

    /// <summary>
    /// Defines an abstraction for interacting with the Docker Engine API.
    /// </summary>
    public interface IContainerService : IAsyncDisposable
    {
        /// <summary>
        /// Starts a stopped container.
        /// </summary>
        /// <param name="containerId">The id or name of the container to start</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>true if container was started successfully, otherwise false</returns>
        Task<bool> StartAsync(string containerId, CancellationToken ct = default);


        /// <summary>
        /// Stop a running container. 
        /// </summary>
        /// <param name="containerId">The id or name of the container to stop. Sends SIGTERM, and then SIGKILL if not killed.</param>
        /// <param name="timeout">Time to wait before forcibly kill the container</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns></returns>
        Task StopAsync(string containerId, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// Removes a container for the Docker host
        /// </summary>
        /// <param name="containerId">The id or name of the container to remove.</param>
        /// <param name="force">If true, forcibly removes containers that is running.</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns></returns>
        Task RemoveAsync(string containerId, bool force = false, CancellationToken ct = default);

        /// <summary>
        /// Restarts a running container.
        /// Stops, waits, then starts it again.
        /// </summary>
        /// <param name="containerId">The id or name of the container to remove.</param>
        /// <param name="timeout">Time to wait before forcibly kill the container</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns></returns>
        Task RestartAsync(string containerId, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// Immediately kills a running container (send SIGKILL)
        /// </summary>
        /// <param name="containerId">The id or name of the container to remove.</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns></returns>
        Task KillContainer(string containerId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves detailed information about a container with the specified ID.
        /// </summary>
        /// <remarks>Method to retrieve metadata and runtime details about a container, such as
        /// its status, configuration, and resource usage.</remarks>
        /// <param name="containerId">The unique identifier of the container to inspect.</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>A <see cref="ContainerInfo"/> object containing detailed information about the specified container.</returns>
        Task<ContainerInfo> InspectAsync(string containerId, CancellationToken ct = default);

        /// <summary>
        /// Lists docker containers existing on the host, optionally filtered by name.
        /// </summary>
        /// <param name="all">If true, includes stopped containers</param>
        /// <param name="nameFilter">Optional container name to filter by</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Returns a list of <see cref="ContainerInfo"/> objects.</returns>
        Task<IList<ContainerInfo>> ListContainersAsync(
            bool all = false,
            string? nameFilter = null,
            CancellationToken ct = default);

        /// <summary>
        /// Streams logs as text lines into a Channel.
        /// </summary>
        /// <param name="containerId">The id or name of the container to log</param>
        /// <param name="follow">Keep log stream open for new lines (similair to <c>docker logs -f</c>)</param>
        /// <param name="tail">"all" or a number as a string. Same as docker logs --tail.</param>
        /// <param name="tty">true if container was started with TTY, if so stdout/stderr cannot be split</param>
        /// <param name="ct">Cancellation token to stop reading</param>
        /// <returns>Returns a channel reader that yields tuples of (IsStdErr, Line)</returns>
        Task<ChannelReader<(bool IsStdErr, string Line)>> StreamLogsAsync(
            string containerId,
            bool follow = true,
            string tail = "all",
            bool tty = false,
            CancellationToken ct = default);

        /// <summary>
        /// Runs a command inside a running container and returns (exitCode, stdout, stderr).
        /// Set shell=null to pass a raw argv (e.g., ["sh","-lc","echo test"]).
        /// </summary>
        /// <param name="containerId">The id or name of the container</param>
        /// <param name="cmd">Command arguments (e.g., ["sh", "-c", "echo hello"])</param>
        /// <param name="tty">If true, runs a command with TTY enabled (merged output stream)</param>
        /// <param name="ct">Cancellation token to stop reading</param>
        /// <returns>Returns a tuple of (ExitCode, StdOut, StdErr).</returns>
        Task<(long ExitCode, string StdOut, string StdErr)> ExecAsync(
            string containerId,
            IReadOnlyList<string> cmd,
            bool tty = false,
            CancellationToken ct = default);

        /// <summary>
        /// Runs a command inside a running container and instead streams the output as it is produced.
        /// Similar (same impl) to ExecAsync but returns output line-by-line as a Channel instead of waiting for completion.
        /// The exit code can be retrieved by reading until the channel completes, then inspecting the exec.
        /// </summary>
        /// <param name="containerId">The id or name of the container</param>
        /// <param name="cmd">Command arguments (e.g., ["sh", "-c", "echo hello"])</param>
        /// <param name="tty">If true, runs a command with TTY enabled (merged output stream)</param>
        /// <param name="ct">Cancellation token to stop reading</param>
        /// <returns>Returns a tuple containing a channel reader that yields (IsStdErr, Line) tuples and a task that completes with the exit code when the command finishes</returns>
        Task<(ChannelReader<(bool IsStdErr, string Line)> Output, Task<long> ExitCodeTask)> StreamExecAsync(
            string containerId,
            IReadOnlyList<string> cmd,
            bool tty = false,
            CancellationToken ct = default);
    }
}
