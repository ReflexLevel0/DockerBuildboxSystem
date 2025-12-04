using System;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    public interface ISettingsService
    {
        string SourceFolderPath { get; set; }
        string SyncOutFolderPath { get; set; }
        
        event EventHandler<string> SourcePathChanged;
        
        Task LoadSettingsAsync();
        Task SaveSettingsAsync();
    }
}
