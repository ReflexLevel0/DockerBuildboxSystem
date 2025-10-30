using System.Windows;
using System.ComponentModel;
using DockerBuildBoxSystem.ViewModels.Main;

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
            //cleanup child UserControls by calling their cleanup methods
            //Subscribing to Dispatcher.ShutdownStarted doesn't work sometimes and is really random from testing,
            //by manually calling it it consistently works atleast... dont know if there are a better way to solve this.
            if (ContainerConsoleControl != null)
                await ContainerConsoleControl.CleanupAsync();

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
}