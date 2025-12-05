using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public class SyncIgnoreService : ISyncIgnoreService
    {
        private readonly string _filePath;
        private readonly IExternalProcessService _externalProcessService;

        public SyncIgnoreService(IExternalProcessService externalProcessService,  string filename = ".syncignore")
        {
            _externalProcessService = externalProcessService;
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", filename);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, "");
            }
        }

        public async Task<List<string>> LoadSyncIgnoreAsync()
        {
            var result = new List<string>();
            if (!File.Exists(_filePath))
            {
                return result;
            }

            var lines = await File.ReadAllLinesAsync(_filePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                {
                    continue;
                }
                result.Add(line.Trim());
            }
            return result;

        }

        public void OpenSyncIgnore()
        {
            _externalProcessService.OpenFileInEditor(_filePath);
        }

        public async Task SaveSyncIgnoreAsync(List<string> syncIgnore)
        {
            await File.WriteAllLinesAsync(_filePath,
                syncIgnore.Where(line => !string.IsNullOrWhiteSpace(line)));
        }
    }
}
