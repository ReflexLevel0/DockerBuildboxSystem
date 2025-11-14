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
        private (ChannelReader<(bool IsStdErr, string Line)> Output, ChannelWriter<string> Input, Task<long> ExitCodeTask)? _reader;

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

        public Task<long> ExitCode => _reader?.ExitCodeTask ?? Task.FromResult(-1L);
        private ChannelWriter<string>? InputWriter => _reader?.Input;

        //force TTY for exec sessions - used for interactive shells such python
        private bool _forceTtyExec = true;
        private bool IsInteractive => IsRunning && InputWriter is not null;

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

            _reader = await svc.StreamExecAsync(containerId, args, containerInfo.Tty || _forceTtyExec, linked.Token);

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

        public async Task<bool> TryWriteToInteractiveAsync(string raw)
        {
            if (!IsInteractive) return false;

            try
            {
                await InputWriter!.WriteAsync(raw);
                return true;
            }
            catch (Exception ex)
            {
                return true;
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
