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
        private readonly ISyncIgnoreService _syncIgnoreService;
        
        private FileSystemWatcher? _watcher;
        private string? _rootPath;
        private string? _containerId;
        private string? _containerRootPath;

        private readonly ConcurrentDictionary<string, DateTime> _lastEventTimes = new();
        private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
        private readonly object _changesLock = new object();

        //captured UI synchronization context for thread-safe ObservableCollection updates
        private SynchronizationContext? _uiContext;

        public ObservableCollection<string> Changes { get; } = new ObservableCollection<string>();

        public FileSyncService(
            IContainerFileTransferService fileTransferService,
            IIgnorePatternMatcher ignorePatternMatcher,
            ISyncIgnoreService syncIgnoreService)
        {
            _fileTransferService = fileTransferService ?? throw new ArgumentNullException(nameof(fileTransferService));
            _ignorePatternMatcher = ignorePatternMatcher ?? throw new ArgumentNullException(nameof(ignorePatternMatcher));
            _syncIgnoreService = syncIgnoreService ?? throw new ArgumentNullException(nameof(syncIgnoreService));
        }

        public void Configure(string path, string containerId, string containerRootPath = "/data/")
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path cannot be empty", nameof(path));
            if (string.IsNullOrWhiteSpace(containerId)) throw new ArgumentException("ContainerId cannot be empty", nameof(containerId));
            if (string.IsNullOrWhiteSpace(containerRootPath)) throw new ArgumentException("ContainerRootPath cannot be empty", nameof(containerRootPath));

            _rootPath = Path.GetFullPath(path);
            _containerId = containerId;
            _containerRootPath = containerRootPath;
        }

        public async Task StartWatchingAsync(string path, string containerId, string containerRootPath = "/data/")
        {
            _uiContext = SynchronizationContext.Current;

            Configure(path, containerId, containerRootPath);

            if (!Directory.Exists(_rootPath))
            {
                Log($"Error: Directory does not exist: {_rootPath}");
                return;
            }

            //Stop if already running
            StopWatching();

            //Load ignore patterns before starting the watcher
            await LoadIgnorePatternsAsync();

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
        // Cleans the target directory in the container, excluding specified paths
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
        // Performs a full sync of the host directory to the container
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

                //verify we have a valid container path before syncing
                if (string.IsNullOrWhiteSpace(_containerRootPath))
                {
                    throw new InvalidOperationException("Container root path is not configured.");
                }

                //copy temp folder to Docker
                var (success, error) = await _fileTransferService.CopyDirectoryToContainerAsync(_containerId, tempRoot, _containerRootPath, ct);

                ct.ThrowIfCancellationRequested();

                if (success)
                {
                    Log($"Full Folder Sync -> {_containerId}:{_containerRootPath} | Success");
                }
                else
                {
                    Log($"Full Folder Sync Failed | {error}");
                    throw new InvalidOperationException($"Full Folder Sync Failed: {error}");
                }
            }
            catch (OperationCanceledException)
            {
                Log("[force-sync] Cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Log("EXCEPTION during full folder copy: " + ex.Message);
                throw;
            }
            finally
            {
                //clean up temp folder with retry logic
                await DeleteTempFolderWithRetryAsync(tempRoot);
            }
        }

        /// <summary>
        /// Attempts to delete the temp folder with retry logic to handle file locks.
        /// </summary>
        private async Task DeleteTempFolderWithRetryAsync(string tempRoot)
        {
            if (!Directory.Exists(tempRoot))
                return;

            const int maxRetries = 3;
            const int delayMs = 500;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    //give time for file handles to release
                    if (i > 0)
                        await Task.Delay(delayMs);

                    Directory.Delete(tempRoot, true);
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    //retry on IO exceptions (file in use, access denied)
                    continue;
                }
                catch (UnauthorizedAccessException) when (i < maxRetries - 1)
                {
                    //retry on access denied
                    continue;
                }
                catch (Exception ex)
                {
                    Log($"Failed to delete temp folder (attempt {i + 1}/{maxRetries}): {ex.Message}");
                    if (i == maxRetries - 1)
                        Log($"Warning: Temp folder may remain at: {tempRoot}");
                    return;
                }
            }
        }
        // Performs a full sync from the container to the host directory
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
        // Recursively copies non-ignored files to a temporary directory
        // Used for preparing files for ForceSync to container for performance reasons
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

        /// <summary>
        /// Asynchronously loads ignore patterns from the configured source and updates the pattern matcher.
        /// </summary>
        public async Task LoadIgnorePatternsAsync()
        {
            var patterns = await _syncIgnoreService.LoadSyncIgnoreAsync();
            var patternsString = string.Join(Environment.NewLine, patterns);
            _ignorePatternMatcher.LoadPatterns(patternsString);
            Log($"Sync Ignore file loaded with {patterns.Count()} exclusions.");
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
            bool oldIgnored = IsIgnored(e.OldFullPath);
            bool newIgnored = IsIgnored(e.FullPath);

            //if both files are ignored then there is nothing to be done
            if (oldIgnored && newIgnored)
                return;

            //if old name was ignored but new is not, treat as a new file creation on the container
            //Why? well the old file was ignored and doesn't exist on the container (if I think of this correctly), so there are no file there with that name.
            //This can use OnFileChanged logic, quick fix to get thingss working.
            if (oldIgnored && !newIgnored)
            {
                if (IsDuplicateEvent(e.FullPath, "Copy"))
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
                return;
            }

            //if old name was not ignored but new is, treat as a deletion
            //This can use OnFileDeleted logic, quick fix to get thingss working.
            if (!oldIgnored && newIgnored)
            {
                if (IsDuplicateEvent(e.OldFullPath, "Deleted"))
                    return;

                string oldContainerPath = ToContainerPath(e.OldFullPath);
                var (success, error) = await _fileTransferService.DeleteInContainerAsync(_containerId!, oldContainerPath);
                if (success)
                    Log($"Deleted {e.OldFullPath} (renamed to ignored path)");
                else
                    Log($"Delete Failed: {e.OldFullPath} | {error}");
                return;
            }

            //both paths are not ignored, perform a rename (the intended way)
            if (IsDuplicateEvent(e.FullPath, "Renamed"))
                return;

            string oldPath = ToContainerPath(e.OldFullPath);
            string newPath = ToContainerPath(e.FullPath);

            var (renameSuccess, renameError) = await _fileTransferService.RenameInContainerAsync(_containerId!, oldPath, newPath);
            if (renameSuccess)
                Log($"Renamed {e.OldFullPath} -> {e.FullPath}");
            else
                Log($"Rename Failed: {e.OldFullPath} -> {e.FullPath} | {renameError}");
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Log($"Watcher Error: {e.GetException().Message}");
        }

        private bool IsIgnored(string path)
        {
            return _ignorePatternMatcher.IsIgnored(path);
        }

        // Checks if an event is a duplicate within a debounce interval
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
            void AddToChanges()
            {
                lock (_changesLock)
                {
                    Changes.Add($"{msg}");
                }
            }

            //f we have a UI context and we're not on the UI thread, dispatch to UI thread
            if (_uiContext != null && SynchronizationContext.Current != _uiContext)
            {
                _uiContext.Post(_ => AddToChanges(), null);
            }
            else
            {
                AddToChanges();
            }
        }

        public void Dispose()
        {
            StopWatching();
        }
    }
}
