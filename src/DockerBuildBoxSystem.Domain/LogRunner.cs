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

        // ANSI escape code, RegexOptions.Compiled for better performance compiling regex 
        private static readonly Regex AnsiRegex =
            new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])|\a|\r", RegexOptions.Compiled);

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

            var containerInfo = await svc.InspectAsync(containerId, linked.Token);

            //Whether to use TTY mode based on container settings
            bool useTty = containerInfo.Tty;

            _reader = await svc.StreamLogsAsync(
                containerId,
                follow: true,
                tty: useTty,
                ct: linked.Token);
            IsRunning = true;

            try
            {
                await foreach (var (isStdErr, line) in _reader.ReadAllAsync(linked.Token))
                {
                    if (line is null) continue;
                    // Clean ANSI escape sequences
                    var cleanLine = CleanAnsiFromLogs(line);

                    // skip empty lines after cleaning
                    if (string.IsNullOrWhiteSpace(cleanLine))
                        continue;

                    yield return (isStdErr, cleanLine);
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

        /// <summary>
        /// Removes ANSI escape sequences from the provided log content.
        /// </summary>
        /// <param name="logs">The log content as a string, which may contain ANSI escape sequences.</param>
        /// <returns>A string with all ANSI escape sequences removed.</returns>
        private static string CleanAnsiFromLogs(string logs)
            => AnsiRegex.Replace(logs, "");

    }
}
