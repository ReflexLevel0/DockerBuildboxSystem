using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Provides a method for interacting with external processes, such as opening files in an editor or opening 
    /// a container in windows terminal.
    /// </summary>
    public interface IExternalProcessService
    {
        void StartProcess(string command, string arguments);
    }
}
