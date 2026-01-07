using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public class FileSyncService : IFileSyncService
    {
        private readonly IContainerFileTransferService _fileTransferService;
        private readonly IIgnorePatternMatcher _ignorePatternMatcher;
        
        private FileSystemWatcher? _watcher;
        private string? _rootPath;
        private string? _containerId;
        private string? _containerRootPath;

        private readonly ConcurrentDictionary<string, DateTime> _lastEventTimes = new();
        private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
        private readonly object _changesLock = new object();

        public ObservableCollection<string> Changes { get; } = new ObservableCollection<string>();

        public FileSyncService(
            IContainerFileTransferService fileTransferService,
            IIgnorePatternMatcher ignorePatternMatcher)
        {
            _fileTransferService = fileTransferService ?? throw new ArgumentNullException(nameof(fileTransferService));
            _ignorePatternMatcher = ignorePatternMatcher ?? throw new ArgumentNullException(nameof(ignorePatternMatcher));
        }

        public void Configure(string path, string containerId, string containerRootPath = "/data/")
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path cannot be empty", nameof(path));
            if (string.IsNullOrWhiteSpace(containerId)) throw new ArgumentException("ContainerId cannot be empty", nameof(containerId));

            _rootPath = Path.GetFullPath(path);
            _containerId = containerId;
            _containerRootPath = containerRootPath;
        }

        public void StartWatching(string path, string containerId, string containerRootPath = "/data/")
        {
            Configure(path, containerId, containerRootPath);

            if (!Directory.Exists(_rootPath))
            {
                Log($"Error: Directory does not exist: {_rootPath}");
                return;
            }

            //Stop if already running
            StopWatching();

            _watcher = new FileSystemWatcher(_rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size
            };

            _watcher.Created += OnCreated;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;

            _watcher.EnableRaisingEvents = true;

            Log($"Started watching: {_rootPath} -> Container: {_containerId} ({_containerRootPath})");
        }

        public void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnCreated;
                _watcher.Changed -= OnChanged;
                _watcher.Deleted -= OnDeleted;
                _watcher.Renamed -= OnRenamed;
                _watcher.Error -= OnError;
                _watcher.Dispose();
                _watcher = null;
                Log("Stopped watching.");
            }
        }

        public void PauseWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                Log("Paused watching.");
            }
        }

        public void ResumeWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = true;
                Log("Resumed watching.");
            }
        }

        public async Task CleanDirectoryAsync(IEnumerable<string>? excludedPaths, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(_containerId) || string.IsNullOrWhiteSpace(_containerRootPath))
            {
                Log("Error: Container not configured.");
                return;
            }

            Log($"Cleaning container directory: {_containerRootPath} with {excludedPaths?.Count() ?? 0} exclusions.");
            var (cleanSuccess, cleanError) = await _fileTransferService.EmptyDirectoryInContainerAsync(_containerId, _containerRootPath, excludedPaths, ct);
            ct.ThrowIfCancellationRequested();
            if (!cleanSuccess)
            {
                Log($"Warning: Failed to clean container directory: {cleanError}.");
            }
            else
            {
                Log("Container directory cleaned successfully!");
            }
        }

        public async Task ForceSyncAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
            {
                Log("Error: Invalid directory path.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_containerId))
            {
                Log("Error: Container ID is not set.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_containerRootPath))
            {
                Log("Error: Sync path is not set.");
                return;
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "FileWatcherTemp_" + Guid.NewGuid());

            try
            {
                Log("Starting Force Sync...");
                Directory.CreateDirectory(tempRoot);

                //copy only non-ignored files into temp folder
                CopyToTempRecursive(_rootPath, tempRoot, ct);

                //copy temp folder to Docker
                var (success, error) = await _fileTransferService.CopyDirectoryToContainerAsync(_containerId, tempRoot, _containerRootPath, ct);

                ct.ThrowIfCancellationRequested();

                if (success)
                    Log($"Full Folder Sync -> {_containerId}:{_containerRootPath} | Success");
                else
                    Log($"Full Folder Sync Failed | {error}");
            }
            catch (OperationCanceledException)
            {
                Log("[force-sync] Cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Log("EXCEPTION during full folder copy: " + ex.Message);
            }
            finally
            {
                try
                {
                    if(Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, true);
                }
                catch (Exception ex)
                {
                    Log("Failed to delete temp folder: " + ex.Message);
                }
            }
        }

        public async Task ForceSyncFromContainerAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(_rootPath))
            {
                Log("Error: Host path is not set.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_containerId))
            {
                Log("Error: Container ID is not set.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_containerRootPath))
            {
                Log("Error: Container sync path is not set.");
                return;
            }

            try
            {
                Log("Starting Force Sync from Container...");

                // Ensure target directory exists
                if (!Directory.Exists(_rootPath))
                {
                    Directory.CreateDirectory(_rootPath);
                }

                // Copy directly from container to host folder
                var (success, error) = await _fileTransferService.CopyDirectoryFromContainerAsync(_containerId, _containerRootPath, _rootPath, ct);

                ct.ThrowIfCancellationRequested();

                if (success)
                    Log($"Full Folder Sync <- {_containerId}:{_containerRootPath} | Success");
                else
                    Log($"Failed to copy from container: {error}");
            }
            catch (OperationCanceledException)
            {
                Log("[force-sync-from-container] Cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Log("EXCEPTION during container-to-host sync: " + ex.Message);
            }
        }

        private void CopyToTempRecursive(string sourceDir, string tempRoot, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                ct.ThrowIfCancellationRequested();

                if (IsIgnored(file))
                    continue;

                string relative = Path.GetRelativePath(_rootPath!, file);
                string targetFile = Path.Combine(tempRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

                try
                {
                    File.Copy(file, targetFile, true);
                }
                catch (Exception ex)
                {
                    Log($"ERROR copying file {file}: {ex.Message}");
                }
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                ct.ThrowIfCancellationRequested();

                if (IsIgnored(subDir))
                    continue;

                CopyToTempRecursive(subDir, tempRoot, ct);
            }
        }

        public void UpdateIgnorePatterns(string patterns)
        {
            _ignorePatternMatcher.LoadPatterns(patterns);
            Log("Updated ignore patterns.");
        }
        private string DockerCreateDirectory(string containerDir)
        {
            return RunDockerCommand($"exec TestContainer mkdir -p \"{containerDir}\"");
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (IsIgnored(e.FullPath) || IsDuplicateEvent(e.FullPath, "Created"))
                return;

            // Directory
            if (Directory.Exists(e.FullPath))
            {
                string dir = ToContainerPath(e.FullPath);
                string result = DockerCreateDirectory(dir);
                Log($"Dir created {e.FullPath} → {dir} | {result}");
                return;
            }

            // File
            if (File.Exists(e.FullPath))
            {
                string file = ToContainerPath(e.FullPath);
                string result = DockerCopy(e.FullPath, file);
                Log($"File created {e.FullPath} → {file} | {result}");
            }
        }


        private async void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (IsIgnored(e.FullPath) || IsDuplicateEvent(e.FullPath, "Copy"))
                return;

            if (File.Exists(e.FullPath))
                return;
            
            string containerPath = ToContainerPath(e.FullPath);
            var (success, error) = await _fileTransferService.CopyToContainerAsync(_containerId!, e.FullPath, containerPath);
            if (success)
                Log($"Copy {e.FullPath} -> {containerPath}");
            else
                Log($"Copy Failed: {e.FullPath} -> {containerPath} | {error}");
            
        }

        private async void OnDeleted(object sender, FileSystemEventArgs e)
        {
            if (IsIgnored(e.FullPath) || IsDuplicateEvent(e.FullPath, "Deleted"))
                return;

            string containerPath = ToContainerPath(e.FullPath);
            var (success, error) = await _fileTransferService.DeleteInContainerAsync(_containerId!, containerPath);
            if (success)
                Log($"Deleted {e.FullPath}");
            else
                Log($"Delete Failed: {e.FullPath} | {error}");
        }

        private async void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (IsIgnored(e.FullPath) || IsDuplicateEvent(e.FullPath, "Renamed"))
                return;

            string oldPath = ToContainerPath(e.OldFullPath);
            string newPath = ToContainerPath(e.FullPath);

            var (success, error) = await _fileTransferService.RenameInContainerAsync(_containerId!, oldPath, newPath);
            if (success)
                Log($"Renamed {e.OldFullPath} -> {e.FullPath}");
            else
                Log($"Rename Failed: {e.OldFullPath} -> {e.FullPath} | {error}");
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Log($"Watcher Error: {e.GetException().Message}");
        }

        private bool IsIgnored(string path)
        {
            return _ignorePatternMatcher.IsIgnored(path);
        }

        private bool IsDuplicateEvent(string path, string changeType)
        {
            string key = $"{changeType}:{path}";
            DateTime now = DateTime.Now;

            if (_lastEventTimes.TryGetValue(key, out var lastTime))
            {
                if (now - lastTime < _debounceInterval)
                    return true;
            }

            _lastEventTimes[key] = now;
            return false;
        }

        private string ToContainerPath(string fullHostPath)
        {
            string relative = Path.GetRelativePath(_rootPath!, fullHostPath).Replace('\\', '/');
            string root = _containerRootPath!.EndsWith("/") ? _containerRootPath : _containerRootPath + "/";
            return root + relative;
        }

        private void Log(string msg)
        {
            lock (_changesLock)
            {
                Changes.Add($"{msg}");
            }
        }

        public void Dispose()
        {
            StopWatching();
        }
    }
}
