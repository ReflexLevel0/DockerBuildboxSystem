using DockerBuildBoxSystem.Contracts;
using System;
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

        public void StartWatching(string path, string containerId, string containerRootPath = "/data/")
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path cannot be empty", nameof(path));
            if (string.IsNullOrWhiteSpace(containerId)) throw new ArgumentException("ContainerId cannot be empty", nameof(containerId));

            if (!Directory.Exists(path))
            {
                Log($"Error: Directory does not exist: {path}");
                return;
            }

            _rootPath = Path.GetFullPath(path);
            _containerId = containerId;
            _containerRootPath = containerRootPath;

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

            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
            _watcher.Deleted += OnFileDeleted;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += OnError;

            _watcher.EnableRaisingEvents = true;

            Log($"Started watching: {_rootPath} -> Container: {_containerId} ({_containerRootPath})");
        }

        public void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileChanged;
                _watcher.Changed -= OnFileChanged;
                _watcher.Deleted -= OnFileDeleted;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Error -= OnError;
                _watcher.Dispose();
                _watcher = null;
                Log("Stopped watching.");
            }
        }

        public void UpdateIgnorePatterns(string patterns)
        {
            _ignorePatternMatcher.LoadPatterns(patterns);
            Log("Updated ignore patterns.");
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (IsIgnored(e.FullPath) || IsDuplicateEvent(e.FullPath, "Copy"))
                return;

            if (File.Exists(e.FullPath))
            {
                string containerPath = ToContainerPath(e.FullPath);
                var (success, error) = await _fileTransferService.CopyToContainerAsync(_containerId!, e.FullPath, containerPath);
                if (success)
                    Log($"Copy {e.FullPath} -> {containerPath}");
                else
                    Log($"Copy Failed: {e.FullPath} -> {containerPath} | {error}");
            }
        }

        private async void OnFileDeleted(object sender, FileSystemEventArgs e)
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

        private async void OnFileRenamed(object sender, RenamedEventArgs e)
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
                Changes.Add($"[{DateTime.Now:T}] {msg}");
            }
        }

        public void Dispose()
        {
            StopWatching();
        }
    }
}
