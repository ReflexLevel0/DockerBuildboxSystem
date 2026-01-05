using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public sealed class LogRunner : ILogRunner
    {
        //logs streaming
        private CancellationTokenSource? _logsCts = new();
        private ChannelReader<(bool, string)>? _reader;

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                RunningChanged?.Invoke(this, value);
            }
        }

        public event EventHandler<bool>? RunningChanged;

        /// <summary>
        /// Asynchronously streams log output lines from a running container.
        /// </summary>
        /// <param name="svc">The container service used to interact with the target container.</param>
        /// <param name="containerId">The unique identifier of the container from which to stream logs. Cannot be null.</param>
        /// <param name="args">Optional arguments to customize log streaming behavior. May be <see langword="null"/> to use default
        /// settings.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the log streaming operation.</param>
        /// <returns>An asynchronous stream of tuples, where each tuple contains a Boolean indicating whether the line is from
        /// standard error (<see langword="true"/>) or standard output (<see langword="false"/>), and the log line as a
        /// string.</returns>
        public async IAsyncEnumerable<(bool IsStdErr, string Line)> RunAsync(IContainerService svc, 
            string containerId,
            string[]? args = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            //Stop existing logs if it is currently running
            _logsCts?.Cancel();
            _logsCts?.Dispose();
            _logsCts = new CancellationTokenSource();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_logsCts.Token, ct);

            var containerInfo = await svc.InspectAsync(containerId, linked.Token).ConfigureAwait(false);

            //Whether to use TTY mode based on container settings
            bool useTty = containerInfo.Tty;

            _reader = await svc.StreamLogsAsync(
                containerId,
                follow: true,
                tty: useTty,
                ct: linked.Token).ConfigureAwait(false);
            IsRunning = true;

            try
            {
                await foreach (var (isStdErr, line) in _reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
                {
                    yield return (isStdErr, line);
                }
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// Initiates an asynchronous operation to stop the logging process.
        /// </summary>
        public async Task StopAsync()
        {
            _logsCts?.Cancel();
        }

        public ValueTask DisposeAsync()
        {
            _logsCts?.Cancel();
            _logsCts?.Dispose();
            _logsCts = null;
            return ValueTask.CompletedTask;
        }

    }
}
