using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// 
    /// </summary>
    public interface ICommandRunner : IStreamReader
    {
        /// <summary>
        /// Returns exit code of the command (or -1 if there is no exit code)
        /// </summary>
        Task<long> ExitCode { get; }

        /// <summary>
        /// Attempts to write the specified string to the interactive input stream asynchronously.
        /// </summary>
        /// <param name="raw">The string to write to the interactive input stream.</param>
        /// <returns>— <see langword="true"/> if the string was written to the interactive input stream or if an error occurred
        /// during writing; otherwise, <see langword="false"/> if the input stream is not in interactive mode.</returns>
        Task<bool> TryWriteToInteractiveAsync(string raw);

        /// <summary>
        /// Sends an interrupt signal to the interactive process.
        /// </summary>
        Task InterruptAsync();
    }
}
