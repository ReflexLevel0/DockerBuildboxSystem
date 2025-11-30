using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace FileWatcherApp
{
    public partial class MainWindow : Window
    {
        private FileSystemWatcher _watcher;
        private ObservableCollection<string> _changes = new();

        private readonly ConcurrentDictionary<string, DateTime> _lastEventTimes = new();
        private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);

        private List<Regex> _ignorePatterns = new();

        private string _rootPath;

        public MainWindow()
        {
            InitializeComponent();
            ChangesList.ItemsSource = _changes;
        }

        // ============================================================
        // IGNORE PATTERNS
        // ============================================================
        private void LoadIgnorePatterns()
        {
            _ignorePatterns.Clear();
            string[] lines = IgnorePatternsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                string pattern = line.Trim();
                if (pattern.Length == 0) continue;

                if (pattern.StartsWith("./"))
                    pattern = pattern[2..];

                string regexPattern = Regex.Escape(pattern)
                    .Replace(@"\*\*", ".*")
                    .Replace(@"\*", "[^/\\\\]*")
                    .Replace(@"\?", ".");

                if (pattern.EndsWith("/"))
                    regexPattern = $"{regexPattern}.*";

                regexPattern = ".*" + regexPattern + ".*";
                _ignorePatterns.Add(new Regex(regexPattern, RegexOptions.IgnoreCase));
            }
        }

        private IEnumerable<string> GetIgnoreSummary()
        {
            foreach (var r in _ignorePatterns)
                yield return r.ToString();
        }

        private bool IsIgnored(string path)
        {
            string normalized = path.Replace('\\', '/');
            foreach (var regex in _ignorePatterns)
                if (regex.IsMatch(normalized))
                    return true;
            return false;
        }

        // ============================================================
        // WATCHER SETUP
        // ============================================================
        private void StartWatching_Click(object sender, RoutedEventArgs e)
        {
            string path = PathTextBox.Text.Trim();

            if (!Directory.Exists(path))
            {
                MessageBox.Show("Invalid directory path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _rootPath = Path.GetFullPath(path);

            LoadIgnorePatterns();
            SetupWatcher(path);

            _changes.Clear();
            _changes.Add($"[{DateTime.Now:T}] Started watching: {path}");
            _changes.Add($"Ignoring: {string.Join(", ", GetIgnoreSummary())}");
        }

        private void SetupWatcher(string path)
        {
            _watcher?.Dispose();

            _watcher = new FileSystemWatcher(path)
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
            _watcher.EnableRaisingEvents = true;
        }

        // ============================================================
        // DEBOUNCE
        // ============================================================
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

        // ============================================================
        // DOCKER COMMANDS
        // ============================================================
        private string RunDockerCommand(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (!string.IsNullOrWhiteSpace(error))
                    return "ERROR: " + error.Trim();

                return output.Trim();
            }
            catch (Exception ex)
            {
                return "EXCEPTION: " + ex.Message;
            }
        }

        private string DockerCopy(string hostPath, string containerPath)
        {
            return RunDockerCommand($"cp \"{hostPath}\" TestContainer:\"{containerPath}\"");
        }

        private string DockerDelete(string containerPath)
        {
            return RunDockerCommand($"exec TestContainer rm -f \"{containerPath}\"");
        }

        private string DockerRename(string oldPath, string newPath)
        {
            return RunDockerCommand($"exec TestContainer mv \"{oldPath}\" \"{newPath}\"");
        }

        // ============================================================
        // PATH MAPPING
        // ============================================================
        private string ToContainerPath(string fullHostPath)
        {
            string relative = Path.GetRelativePath(_rootPath, fullHostPath)
                                   .Replace('\\', '/');

            return "/data/" + relative;
        }

        // ============================================================
        // FILE EVENTS (with auto Docker syncing)
        // ============================================================
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (IsIgnored(e.FullPath) || IsDuplicateEvent(e.FullPath, "Copy"))
                return;

            // Sync to Docker
            if (File.Exists(e.FullPath))
            {
                string containerPath = ToContainerPath(e.FullPath);
                string result = DockerCopy(e.FullPath, containerPath);

                Log($"Copy {e.FullPath}  →  {containerPath}  |  {result}");
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (IsIgnored(e.FullPath) || IsDuplicateEvent(e.FullPath, "Deleted"))
                return;

            string containerPath = ToContainerPath(e.FullPath);
            string result = DockerDelete(containerPath);

            Log($"Deleted   {e.FullPath}  |  {result}");
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (IsIgnored(e.FullPath) || IsDuplicateEvent(e.FullPath, "Renamed"))
                return;

            string oldPath = ToContainerPath(e.OldFullPath);
            string newPath = ToContainerPath(e.FullPath);

            string result = DockerRename(oldPath, newPath);

            Log($"Renamed: {e.OldFullPath} → {e.FullPath}  |  {result}");
        }

        // ============================================================
        // LOGGING
        // ============================================================
        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                _changes.Add($"[{DateTime.Now:T}] {msg}");
            });
        }
    }
}
