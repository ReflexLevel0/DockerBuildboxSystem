using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Represents a service for managing environment variables.
    /// It provides methods to load, save, and open environment variable files.
    /// </summary>
    public interface IEnvironmentService
    {
        Task<List<EnvVariable>> LoadEnvAsync();
        Task SaveEnvAsync(List<EnvVariable> envVariables);
        void OpenEnvFile();
    }
}
