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
    /// This class manages environment variables. It provides methods to load, save, and open environment variable files.
    /// </summary>
    /// <remarks> It checks for the existence of a .env file in the config directory of the application's base directory.
    /// If the file does not exist, it creates an empty one.
    /// </remarks>
    public class EnvironmentService: IEnvironmentService
    {
        private readonly string _envFilePath;
        private readonly IExternalProcessService _externalProcessService;
        public EnvironmentService(IExternalProcessService externalProcessService, string filename = ".env")
        {
            _externalProcessService = externalProcessService;

            _envFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", filename);
            Directory.CreateDirectory(Path.GetDirectoryName(_envFilePath)!);
            
            if (!File.Exists(_envFilePath))
            {
                File.WriteAllText(_envFilePath, "");
            }
        }

        /// <summary>
        /// Loads environment variables from the .env file asynchronously.
        /// </summary>
        /// <returns> a list of environment variables </returns>
        /// <remarks> This method reads the .env file line by line and parses each line into key-value pairs. </remarks>
        public async Task<List<EnvVariable>> LoadEnvAsync()
        {
            var result = new List<EnvVariable>();
            if (!File.Exists(_envFilePath))
            {
                return result;
            }

            var lines = await File.ReadAllLinesAsync(_envFilePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                {
                    continue;
                }
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    result.Add(new EnvVariable
                    {
                        Key = parts[0].Trim(),
                        Value = parts[1].Trim()
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Saves environment variables to the .env file asynchronously.
        /// </summary>
        /// <param name="envVariables">the list of environment variables to save</param>
        /// <returns>a task representing the asynchronous operation</returns>
        /// <remarks> This method writes the environment variables to the .env file, overwriting any existing content. </remarks>
        public async Task SaveEnvAsync(List<EnvVariable> envVariables)
        {
            var lines = envVariables
                .Select(v => $"{v.Key}={v.Value}")
                .ToList();

            await File.WriteAllLinesAsync(_envFilePath, lines);
        }

        /// <summary>
        /// Opens the environment file in the default text editor for viewing or editing.
        /// </summary>
        /// <remarks>This method launches Notepad to open the environment file specified by the internal
        /// file path.</remarks>
        public void OpenEnvFile()
        {
            _externalProcessService.OpenFileInEditor(_envFilePath);
        }
    }
}
