using DockerBuildBoxSystem.Contracts;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public sealed class CommandRunner : ICommandRunner
    {
        //exec streaming
        private CancellationTokenSource? _execCts = new();
        private (ChannelReader<(bool IsStdErr, string Line)> Output, Task<long> ExitCodeTask)? _reader;

        public bool IsRunning { get; private set; }
        public Task<long> ExitCode => _reader?.ExitCodeTask ?? Task.FromResult(-1L);

        public async IAsyncEnumerable<(bool IsStdErr, string Line)> RunAsync(IContainerService svc, 
            string containerId, 
            string[]? args,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if(args is null || args.Length == 0)
                yield break;

            await StopAsync();

            _execCts?.Cancel();
            _execCts?.Dispose();
            _execCts = new CancellationTokenSource();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_execCts.Token, ct);

            var containerInfo = await svc.InspectAsync(containerId, linked.Token);

            _reader = await svc.StreamExecAsync(containerId, args, containerInfo.Tty, linked.Token);

            IsRunning = true;

            try
            {
                await foreach (var item in _reader.Value.Output.ReadAllAsync(linked.Token))
                {
                    yield return item;
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

            _execCts?.Cancel();
            IsRunning = false;
        }


        public ValueTask DisposeAsync()
        {
            _execCts?.Cancel();
            _execCts?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
