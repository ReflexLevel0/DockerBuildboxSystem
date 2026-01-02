using Docker.DotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public abstract class DockerServiceBase : IAsyncDisposable, IDisposable
    {
        protected readonly IDockerClient Client;

        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="DockerService"/> class.
        /// </summary>
        /// <param name="endpoint">The docker endpoint URI. If null, a platform default is used retrieved from <see cref="GetDefaultDockerUri"/>.</param>
        /// <param name="timeout">Optional timeout for Docker requests. Defaults to 100 seconds.</param>
        protected DockerServiceBase(string? endpoint = null, TimeSpan? timeout = null)
        {
            //Create the client
            Client = new DockerClientConfiguration(
                endpoint is not null ? new Uri(endpoint) : GetDefaultDockerUri(),
                new AnonymousCredentials(),
                default,
                timeout ?? TimeSpan.FromSeconds(100))
                .CreateClient();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DockerServiceBase"/> class with an existing client.
        /// </summary>
        /// <param name="client">An existing Docker client instance.</param>
        protected DockerServiceBase(IDockerClient client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
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

        /// <summary>
        /// Synchronously disposes resources by delegating to <see cref="DisposeAsync"/> and blocking until completion.
        /// </summary>
        public virtual void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            DisposeAsync().AsTask().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the underlying Docker client
        /// </summary>
        /// <returns>A task that represents the dispose operation.</returns>
        public virtual async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            //dispose synchronously
            Client?.Dispose();

            await Task.CompletedTask;

            GC.SuppressFinalize(this);
        }
    }
}
