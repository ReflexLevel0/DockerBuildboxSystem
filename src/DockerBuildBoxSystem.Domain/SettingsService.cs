using DockerBuildBoxSystem.Contracts;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    /// <summary>
    /// Functionality for application settings, since the source folder path and sync out folder path need 
    /// to be shared between viewmodels.
    /// The previuous PersistSourcePathAsync and PersistSyncOutPathAsync methods had similar logic, 
    /// so this class just centralizes and also solves the issue with sharing settings between viewmodels.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly IConfiguration _configuration;
        private string _sourceFolderPath = string.Empty;
        private string _syncOutFolderPath = string.Empty;

        public event EventHandler<string>? SourcePathChanged;

        public string SourceFolderPath
        {
            get => _sourceFolderPath;
            set
            {
                if (_sourceFolderPath != value)
                {
                    _sourceFolderPath = value;
                    SourcePathChanged?.Invoke(this, _sourceFolderPath);
                    _ = SaveSettingsAsync();
                }
            }
        }

        public string SyncOutFolderPath
        {
            get => _syncOutFolderPath;
            set
            {
                if (_syncOutFolderPath != value)
                {
                    _syncOutFolderPath = value;
                    _ = SaveSettingsAsync();
                }
            }
        }

        public SettingsService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task LoadSettingsAsync()
        {
            //1. try to load from in-memory configuration first
            var configSource = _configuration["Application:SourceFolderPath"];
            if (!string.IsNullOrEmpty(configSource))
            {
                _sourceFolderPath = configSource;
            }

            var configSync = _configuration["Application:SyncOutFolderPath"];
            if (!string.IsNullOrEmpty(configSync))
            {
                _syncOutFolderPath = configSync;
            }

            //2. try to load from disk to get the latest state
            try
            {
                var configDir = Path.Combine(AppContext.BaseDirectory, "Config");
                var filePath = Path.Combine(configDir, "appsettings.json");
                
                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    using var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("Application", out var appElem))
                    {
                        if (appElem.TryGetProperty("SourceFolderPath", out var pathElem))
                        {
                            var val = pathElem.GetString();
                            if (!string.IsNullOrWhiteSpace(val)) _sourceFolderPath = val;
                        }
                        if (appElem.TryGetProperty("SyncOutFolderPath", out var syncElem))
                        {
                            var val = syncElem.GetString();
                            if (!string.IsNullOrWhiteSpace(val)) _syncOutFolderPath = val;
                        }
                    }
                }
            }
            catch (Exception)
            {
                //iignore load errors, fall back to defaults
            }
        }

        public async Task SaveSettingsAsync()
        {
            try
            {
                var configDir = Path.Combine(AppContext.BaseDirectory, "Config");
                Directory.CreateDirectory(configDir);
                var filePath = Path.Combine(configDir, "appsettings.json");

                JsonNode? rootNode = null;
                if (File.Exists(filePath))
                {
                    var existing = await File.ReadAllTextAsync(filePath);
                    try { rootNode = JsonNode.Parse(existing); } catch { rootNode = null; }
                }
                
                rootNode ??= new JsonObject();
                
                var appNode = rootNode["Application"] as JsonObject;
                if (appNode == null)
                {
                    appNode = new JsonObject();
                    rootNode["Application"] = appNode;
                }

                appNode["SourceFolderPath"] = _sourceFolderPath;
                appNode["SyncOutFolderPath"] = _syncOutFolderPath;

                var json = rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception)
            {
                //handle save errors
            }
        }
    }
}
