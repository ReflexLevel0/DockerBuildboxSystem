using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    public interface ICommandRunner : IStreamReader
    {
        public Task<long> ExitCode { get; }
    }
}
