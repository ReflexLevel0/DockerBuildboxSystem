using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    public interface IEnvironmentService
    {
        Task<List<EnvVariable>> LoadEnvAsync();
        Task SaveEnvAsync(List<EnvVariable> envVariables);
        void OpenEnvFileInEditor();
    }
}
