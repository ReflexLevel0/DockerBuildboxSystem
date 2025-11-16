using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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

        public async Task StopAsync()
        {
            if (!IsRunning) return;

            _logsCts?.Cancel();
            IsRunning = false;
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
