using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using DockerBuildBoxSystem.ViewModels.Common;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using DockerBuildBoxSystem.Contracts;

namespace DockerBuildBoxSystem.ViewModels.Main;

/// <summary>
/// ViewModel for the main window. Handles the primary UI logic and coordinates interactions between different parts of the application.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IConfiguration _configuration;
    private readonly ISettingsService _settingsService;
    // Suppress persisting SourcePath while we are loading the initial value
    private bool _isLoadingSourcePath;

    /// <summary>
    /// Event raised when the application should exit.
    /// </summary>
    public event EventHandler? ExitRequested;

    /// The selected source path from the folder picker.
    /// </summary>
    [ObservableProperty]
    private string? _sourcePath;

    /// <summary>
    /// The selected sync-out path from the folder picker.
    /// </summary>
    [ObservableProperty]
    private string? _syncOutPath;

    public MainViewModel(IConfiguration configuration, ISettingsService settingsService)
    {
        _configuration = configuration;
        _settingsService = settingsService;
        
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

            // Load persisted source folder path if available
            await LoadSourcePathFromConfigAsync();
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

    /// <summary>
    /// Reads the persisted SourceFolderPath from appsettings.json and assigns it to SourcePath if present.
    /// </summary>
    private async Task LoadSourcePathFromConfigAsync()
    {
        try
        {
            await _settingsService.LoadSettingsAsync();
            
            _isLoadingSourcePath = true;
            if (!string.IsNullOrWhiteSpace(_settingsService.SourceFolderPath))
            {
                SourcePath = _settingsService.SourceFolderPath;
            }
            if (!string.IsNullOrWhiteSpace(_settingsService.SyncOutFolderPath))
            {
                SyncOutPath = _settingsService.SyncOutFolderPath;
            }
            _isLoadingSourcePath = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[load-sourcepath-error] {ex.Message}", true);
        }
    }

    partial void OnSourcePathChanged(string? value)
    {
        if (_isLoadingSourcePath) return; // skip persisting initial load value
        if (value == null) return;
        
        _settingsService.SourceFolderPath = value;
    }

    partial void OnSyncOutPathChanged(string? value)
    {
        if (_isLoadingSourcePath) return;
        if (value == null) return;
        
        _settingsService.SyncOutFolderPath = value;
    }

    /// <summary>
    /// Opens a folder picker (must run on UI thread) and assigns the selected path.
    /// Removed background Task.Run to ensure the dialog actually shows.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectSourcePath()
    {
        try
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a source folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            var result = dialog.ShowDialog(); // Runs on UI thread
            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                SourcePath = dialog.SelectedPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[folder-picker-error] {ex.Message}", true, true);
        }

        await Task.CompletedTask; // Maintain async signature for RelayCommand
    }

    /// <summary>
    /// Opens a folder picker for SyncOut directory and assigns the selected path.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectSyncOutPath()
    {
        try
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a sync-out folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            var result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                SyncOutPath = dialog.SelectedPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[folder-picker-error] {ex.Message}", true, true);
        }

        await Task.CompletedTask;
    }
}

