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

    /// <summary>
    /// Event raised when the application should exit.
    /// </summary>
    public event EventHandler? ExitRequested;

    public MainViewModel(IConfiguration configuration)
    {
        _configuration = configuration;
        
        //load title from configuration
        var appName = _configuration["Application:Name"] ?? "Docker BuildBox System";
        var version = _configuration["Application:Version"];
        Title = string.IsNullOrEmpty(version) ? appName : $"{appName} v{version}";
    }

    private bool CanRunWhenIdle() => !IsBusy;

    /// <summary>
    /// Command to handle application initialization, which is called when the main window is loaded.
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
    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanRunWhenIdle), IncludeCancelCommand = true)]
    private async Task ShutdownAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            ct.ThrowIfCancellationRequested();

            //TODO:Cleanup resources
            //ex: stop any running builds, cleanup Docker containers, save user preferences
            await Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            //... handle any cancellation specific logic
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Command to exit the application.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanRunWhenIdle))]
    private async Task ExitAsync()
    {
        //raise the exit event to close the application
        ExitRequested?.Invoke(this, EventArgs.Empty);

        await Task.Yield();
    }

    /// <summary>
    /// Disposes of the MainViewModel asynchronously, cleaning up event handlers and resources.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        //clean up event handlers
        ExitRequested = null;
        
        await base.DisposeAsync();
    }
}
