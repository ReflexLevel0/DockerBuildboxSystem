using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public class EnvironmentService: IEnvironmentService
    {
        private readonly string _envFilePath;
        public EnvironmentService(string filename = ".env")
        {
            _envFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", filename);
            Directory.CreateDirectory(Path.GetDirectoryName(_envFilePath)!);

            if (!File.Exists(_envFilePath))
            {
                File.WriteAllText(_envFilePath, "");
            }
        }
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

        public async Task SaveEnvAsync(List<EnvVariable> envVariables)
        {
            var lines = envVariables
                .Select(v => $"{v.Key}={v.Value}")
                .ToList();

            await File.WriteAllLinesAsync(_envFilePath, lines);
        }

        public void OpenEnvFileInEditor()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{_envFilePath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
    }
}
