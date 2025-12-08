using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    /// <summary>
    /// Provides functionality for launching external processes, such as opening files in an editor or executing
    /// commands.
    /// </summary>
    /// <remarks>This service abstracts process launching operations, allowing applications to interact with
    /// external programs in a platform-agnostic manner. Typical use cases include opening files for editing or running
    /// command-line utilities. The implementation may vary depending on the operating system and environment.</remarks>
    public class ExternalProcessService : IExternalProcessService
    {
        /// <summary>
        /// Executes an external process with the specified command and arguments.
        /// </summary>
        /// <remarks>This method starts a new process using the provided command and arguments.
        /// It's used to open files in text editors or run a container in Windows Terminal, among other tasks.</remarks>
        /// <param name="command">the command to execute</param>
        /// <param name="arguments">the arguments to pass to the command</param>
        public void StartProcess(string command, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        
    }
}
