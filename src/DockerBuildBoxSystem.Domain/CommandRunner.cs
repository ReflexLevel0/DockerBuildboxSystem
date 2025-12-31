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
        private readonly IEnvironmentService _environmentService;

        // Track last container and shell
        private string? _lastContainerIdWithEnvs;
        private bool _shellSessionActive;

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;

                // Reset shell session when command stops
                if (!value && _shellSessionActive)
                {
                    _shellSessionActive = false;
                }
                RunningChanged?.Invoke(this, value);
            }
        }

        public event EventHandler<bool>? RunningChanged;

        public Task<long> ExitCode => _reader?.ExitCodeTask ?? Task.FromResult(-1L);
        private ChannelWriter<string>? InputWriter => _reader?.Input;

        //force TTY for exec sessions - used for interactive shells such python
        private bool _forceTtyExec = true;
        private bool IsInteractive => IsRunning && InputWriter is not null;

        public CommandRunner(IEnvironmentService environmentService)
        {
            _environmentService = environmentService;
        }

        public async IAsyncEnumerable<(bool IsStdErr, string Line)> RunAsync(IContainerService svc, 
            string containerId, 
            string[]? args,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if(args is null || args.Length == 0)
                yield break;

            await StopAsync().ConfigureAwait(false);

            _execCts?.Cancel();
            _execCts?.Dispose();
            _execCts = new CancellationTokenSource();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_execCts.Token, ct);

            var containerInfo = await svc.InspectAsync(containerId, linked.Token).ConfigureAwait(false);

            // Check if this is a shell command
            if (IsShell(args))
            {
                //Check if we need to load environment variables
                bool shouldLoadEnvs = _lastContainerIdWithEnvs != containerId // new container
                                      || !_shellSessionActive;                // Bash was exited

                if (shouldLoadEnvs)
                {
                    // Load environment variables
                    var envs = await _environmentService.LoadEnvAsync().ConfigureAwait(false);
                    
                    // Build new args with exports
                    args = BuildShellArgsWithExports(envs);

                    // Track that we have an active shell with envs loaded for this container
                    _lastContainerIdWithEnvs = containerId;
                    _shellSessionActive = true;
                }
                else
                {
                    // Continuing existing shell session with envs loaded
                    _shellSessionActive = true;
                }
                    
            }

            _reader = await svc.StreamExecAsync(containerId, args, containerInfo.Tty || _forceTtyExec, linked.Token).ConfigureAwait(false);
            IsRunning = true;
            try
            {
                await foreach (var item in _reader.Value.Output.ReadAllAsync(linked.Token).ConfigureAwait(false))
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
                await InputWriter!.WriteAsync(raw).ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                return true;
            }
        }

        public async Task InterruptAsync()
        {
            if (!IsInteractive) return;
            await TryWriteToInteractiveAsync(AnsiControlChars.ETX.ToString()).ConfigureAwait(false);
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

        /// <summary>
        /// Builds shell arguments with export commands for the given environment variables.
        /// </summary>
        /// <param name="envs">Environmental variables to be exported to the container</param>
        /// <returns>List of commands to be executed in the container</returns>
        private string[] BuildShellArgsWithExports(
            List<EnvVariable> envs)
        {
            // If no environment variables, return default bash command
            if (envs.Count == 0)
                return new[] { "/bin/bash" };

            // Construct export commands
            var exports = string.Join(" && ",
                envs.Select(e =>
                $"export {e.Key}='{Escape(e.Value!)}'"));
            // Return bash command with exports
            return new[]
            {
                "/bin/bash",
                "-l",
                "-c",
                $"{exports} && exec /bin/bash"
            };
            
        }

        /// <summary>
        /// Checks if the provided command arguments represent a shell command.
        /// </summary>
        /// <param name="args"> the command arguments</param>
        /// <returns>true if the command is a shell command; otherwise, false.</returns>
        private static bool IsShell(string[] args)
        {
            if (args.Length == 0)
                return false;

            var cmd = args[0];
            return cmd == "/bin/bash" || cmd.EndsWith("/bash");
        }

        /// <summary>
        /// Replaces single quotes in the value with an escaped version for safe shell usage.
        /// </summary>
        /// <param name="value">the string value to escape</param>
        /// <returns>the escaped string</returns>
        private static string Escape(string value)
        {
            return value.Replace("'", "'\"'\"'");
        }
    }
}
