using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Strong typed representation of docker container states.
    /// https://www.baeldung.com/ops/docker-container-states#bd-possible-states-of-a-docker-container
    /// </summary>
    public enum ContainerState
    {
        //To be on the safe side... in case I missed a state or docker decides to add new ones.
        Unknown = 0,
        Created,
        Running,
        Restarting,
        Exited,
        Paused,
        Dead
    }

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

        /// <summary>
        /// Strong typed state derived from <see cref="State"/>.
        /// </summary>
        public ContainerState StateKind => ParseState(Status);

        /// <summary>
        /// A flag indicating whether the container is currently running or not, based on the <see cref="StateKind"/>.
        /// </summary>
        public bool IsRunning => StateKind == ContainerState.Running;

        private static ContainerState ParseState(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return ContainerState.Unknown;

            return s.Trim().ToLowerInvariant() switch
            {
                "created" => ContainerState.Created,
                "running" => ContainerState.Running,
                "restarting" => ContainerState.Restarting,
                "exited" => ContainerState.Exited,
                "paused" => ContainerState.Paused,
                "dead" => ContainerState.Dead,
                _ => ContainerState.Unknown
            };
        }
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
        /// Creates a new container from the specified options.
        /// </summary>
        /// <param name="options">The options for creating the container.</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>The ID of the created container.</returns>
        Task<string> CreateContainerAsync(ContainerCreationOptions options, CancellationToken ct = default);


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
        /// Executes a command inside a running container and waits for it to complete.
        /// </summary>
        /// <param name="containerId">The id or name of the container</param>
        /// <param name="cmd">Command arguments (e.g., ["sh", "-c", "echo hello"])</param>
        /// <param name="ct">Cancellation token to stop reading</param>
        /// <returns>A tuple containing the exit code, stdout, and stderr</returns>
        Task<(long ExitCode, string Output, string Error)> ExecAsync(
            string containerId,
            IReadOnlyList<string> cmd,
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
        Task<(ChannelReader<(bool IsStdErr, string Line)> Output, ChannelWriter<string> Input, Task<long> ExitCodeTask)> StreamExecAsync(
            string containerId,
            IReadOnlyList<string> cmd,
            bool tty = false,
            CancellationToken ct = default);

        /// <summary>
        /// Copies a file from the host to the container.
        /// </summary>
        /// <param name="containerId">The id or name of the container</param>
        /// <param name="hostPath">The absolute path to the file on the host</param>
        /// <param name="containerPath">The absolute path to the destination in the container</param>
        /// <param name="ct">Cancellation token</param>
        Task CopyFileToContainerAsync(
            string containerId,
            string hostPath,
            string containerPath,
            CancellationToken ct = default);

        /// <summary>
        /// Copies a directory from the host to the container.
        /// </summary>
        /// <param name="containerId">The id or name of the container</param>
        /// <param name="hostPath">The absolute path to the directory on the host</param>
        /// <param name="containerPath">The absolute path to the destination directory in the container</param>
        /// <param name="ct">Cancellation token</param>
        Task CopyDirectoryToContainerAsync(
            string containerId,
            string hostPath,
            string containerPath,
            CancellationToken ct = default);

    }
}
