using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    /// <summary>
    /// Provides functionality to manage the .syncignore file, including loading, saving, and opening the file for
    /// editing. This service enables applications to read and update file synchronization ignore patterns.
    /// </summary>
    /// <remarks>The SyncIgnoreService creates the .syncignore file in the application's configuration
    /// directory if it does not exist. It supports asynchronous operations for reading and writing ignore patterns, and
    /// allows users to open the file in an external editor. This service is typically used to control which files or
    /// directories are excluded from synchronization processes.</remarks>
    public class SyncIgnoreService : ISyncIgnoreService
    {
        private readonly string _filePath;
        private readonly IExternalProcessService _externalProcessService;

        public string FilePath => _filePath;

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

        /// <summary>
        /// Asynchronously loads and parses the list of ignore patterns from the sync ignore file.
        /// </summary>
        /// <remarks>Lines that are empty or start with a '#' character are ignored. Leading and trailing
        /// whitespace is trimmed from each entry.</remarks>
        /// <returns>A list of strings containing non-empty, non-comment lines from the sync ignore file. Returns an empty list
        /// if the file does not exist or contains no valid entries.</returns>
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

        /// <summary>
        /// Opens the sync ignore file in the default text editor.
        /// </summary>
        public void OpenSyncIgnore()
        {
            _externalProcessService.StartProcess("notepad.exe", $"\"{_filePath}\"");
        }

        /// <summary>
        /// Asynchronously saves the specified list of sync ignore patterns to the sync ignore file, excluding any empty
        /// or whitespace-only lines.
        /// </summary>
        /// <remarks>Empty or whitespace-only lines in the provided list are omitted from the saved file.
        /// The method overwrites the existing contents of the sync ignore file with the filtered patterns.</remarks>
        /// <param name="syncIgnore">The list of sync ignore patterns to write to the file. Each entry represents a pattern to be ignored during
        /// synchronization. Cannot be null.</param>
        /// <returns>A task that represents the asynchronous save operation.</returns>
        public async Task SaveSyncIgnoreAsync(List<string> syncIgnore)
        {
            await File.WriteAllLinesAsync(_filePath,
                syncIgnore.Where(line => !string.IsNullOrWhiteSpace(line)));
        }
    }
}
