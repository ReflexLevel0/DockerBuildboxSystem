using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public class ExternalProcessService : IExternalProcessService
    {
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
