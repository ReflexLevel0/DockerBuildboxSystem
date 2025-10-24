using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.ViewModels.Common;
using Microsoft.Extensions.Configuration;

namespace DockerBuildBoxSystem.ViewModels.Main;

/// <summary>
/// ViewModel for the main window. Handles the primary UI logic and coordinates interactions between different parts of the application.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IConfiguration _configuration;

    public MainViewModel(IConfiguration configuration)
    {
        _configuration = configuration;
        
        //load title from configuration
        var appName = _configuration["Application:Name"] ?? "Docker BuildBox System";
        var version = _configuration["Application:Version"];
        Title = string.IsNullOrEmpty(version) ? appName : $"{appName} v{version}";
    }

    /// <summary>
    /// Command to handle application initialization, which iscalled when the main window is loaded.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            //TODO:Initialize application components
            //ex: load available BuildBoxes, check Docker availability, load user preferences
            await Task.CompletedTask;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Command to handle application shutdown cleanup.
    /// </summary>
    [RelayCommand]
    private async Task ShutdownAsync()
    {
        //TODO:Cleanup resources
        //ex: stop any running builds, cleanup Docker containers, save user preferences
        await Task.CompletedTask;
    }
}
