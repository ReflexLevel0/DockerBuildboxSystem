using DockerBuildBoxSystem.ViewModels.Main;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;

namespace DockerBuildBoxSystem.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isClosing;

    /// <summary>
    /// Initializes a new instance of the MainWindow class. The ViewModel is injected via dependency injection.
    /// </summary>
    /// <param name="viewModel">The MainViewModel instance injected by dependency injection container.</param>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        
        _viewModel = viewModel;
        
        //Set the DataContext to the injected ViewModel
        DataContext = viewModel;
        
        //Subscribe to exit request event
        viewModel.ExitRequested += (s, e) => Application.Current.Shutdown();
        
        //Initialize the ViewModel when the window loads
        Loaded += async (s, e) => await viewModel.InitializeCommand.ExecuteAsync(null);

        //Cleanup when window is closing (before it closes)
        Closing += OnWindowClosing;
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isClosing)
            return;

        //cancel the close temporarily to perform cleanup
        e.Cancel = true;
        _isClosing = true;
        Closing -= OnWindowClosing;

        try
        {
            // show overlay and let UI render
            if (ShutdownOverlayControl != null)
            {
                ShutdownOverlayControl.Visibility = Visibility.Visible;
                await Task.Yield();
            }

            var start = DateTime.UtcNow;

            //cleanup child UserControls by calling their cleanup methods
            //Subscribing to Dispatcher.ShutdownStarted doesn't work sometimes and is really random from testing,
            //by manually calling it it consistently works atleast... dont know if there are a better way to solve this.
            if (ContainerConsoleControl != null)
                await ContainerConsoleControl.CleanupAsync();

            // ensure overlay is shown for at least 2 seconds (in case of fast shutdown)
            var elapsed = DateTime.UtcNow - start;
            var minDuration = TimeSpan.FromSeconds(2);
            if (elapsed < minDuration)
            {
                await Task.Delay(minDuration - elapsed);
            }

            await _viewModel.ShutdownCommand.ExecuteAsync(null);
        }
        finally
        {

            await Dispatcher.InvokeAsync(() =>
            {
                Close();
            });
        }
    }

    #region Helpers
    /// <summary>
    /// When the user drags something into the window, for instance a folder.
    /// </summary>
    /// <param name="sender">The source of the drag event</param>
    /// <param name="e">Contains relevant information for drag-and-drop events</param>
    private void Path_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = HasFolder(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void SourcePath_Drop(object sender, DragEventArgs e)
    {
        if (HasFolder(e, out var folder))
        {
            _viewModel.SourcePath = folder;
        }
    }

    private void SyncOutPath_Drop(object sender, DragEventArgs e)
    {
        if (HasFolder(e, out var folder))
        {
            _viewModel.SyncOutPath = folder;
        }
    }

    /// <summary>
    /// Determines whether the drag-and-drop event contains a valid folder path.
    /// </summary>
    /// <param name="e">Contains relevant information for drag-and-drop events</param>
    /// <param name="folder">Contains the first valid folder path if one exists, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the drag-and-drop event contains a valid folder path, otherwise <see
    /// langword="false"/>.</returns>
    private bool HasFolder(DragEventArgs e, [NotNullWhen(true)] out string? folder)
    {
        folder = null;

        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            return false;

        folder = paths.FirstOrDefault(Directory.Exists);
        return folder is not null;
    }

    #endregion
}