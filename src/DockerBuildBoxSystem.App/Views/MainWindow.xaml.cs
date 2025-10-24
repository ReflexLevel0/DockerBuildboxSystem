using System.Windows;
using DockerBuildBoxSystem.ViewModels.Main;

namespace DockerBuildBoxSystem.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the MainWindow class. The ViewModel is injected via dependency injection.
    /// </summary>
    /// <param name="viewModel">The MainViewModel instance injected by dependency injection container.</param>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        
        //Set the DataContext to the injected ViewModel
        DataContext = viewModel;
        
        //Initialize the ViewModel when the window loads
        Loaded += async (s, e) => await viewModel.InitializeCommand.ExecuteAsync(null);
        
        //Cleanup when window closes
        Closing += async (s, e) => await viewModel.ShutdownCommand.ExecuteAsync(null);
    }
}