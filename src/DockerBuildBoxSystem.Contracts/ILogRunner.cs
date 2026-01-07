using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Used for streaming log output lines from a running container
    /// </summary>
    public interface ILogRunner : IStreamReader
    {
    }
}
