using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Provides methods for interacting with external processes, such as opening files in an editor or executing system
    /// commands.
    /// </summary>
    public interface IExternalProcessService
    {
        void OpenFileInEditor(string filePath);
        void RunCommand(string command, string arguments);
    }
}
