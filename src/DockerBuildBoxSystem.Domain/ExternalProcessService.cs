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
        /// Opens the specified file in the default text editor (Notepad) on the local machine.
        /// </summary>
        /// <remarks>This method launches Notepad as a separate process to open the specified file. The
        /// file must exist and be accessible; otherwise, Notepad may display an error. This method does not wait for
        /// Notepad to close and does not return a handle to the process.</remarks>
        /// <param name="filePath">The full path to the file to open in Notepad. Cannot be null or empty.</param>
        public void OpenFileInEditor(string filePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{filePath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        /// <summary>
        /// Starts a new process by executing the specified command with the provided arguments using the operating
        /// system shell.
        /// </summary>
        /// <remarks>This method uses the operating system shell to start the process, which may cause the
        /// process window to appear. The caller is responsible for ensuring that the command and arguments are valid
        /// and safe to execute. On some platforms, UseShellExecute may affect process behavior and security.</remarks>
        /// <param name="command">The name or path of the executable file to run. Cannot be null or empty.</param>
        /// <param name="arguments">The command-line arguments to pass to the executable. Can be an empty string if no arguments are required.</param>
        public void RunCommand(string command, string arguments)
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
