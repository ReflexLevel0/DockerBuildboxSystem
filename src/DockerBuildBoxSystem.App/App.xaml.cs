using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DockerBuildBoxSystem.ViewModels.Main;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.Domain;
using DockerBuildBoxSystem.ViewModels.ViewModels;
using DockerBuildBoxSystem.App.UserControls;
using DockerBuildBoxSystem.App.Services;
using Docker.DotNet.Models;
using Newtonsoft.Json;
using System.IO;

namespace DockerBuildBoxSystem.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// Gets the current application instance as an App object.
    /// </summary>
    public static new App Current => (App)Application.Current;

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Services not initialized");

    /// <summary>
    /// Application startup event handler. Configures dependency injection and services.
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        //Build the host with dependency injection
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                //Load configuration from appsettings.json
                //Uses the application's base directory to find config files
                var basePath = AppContext.BaseDirectory;
                config.SetBasePath(basePath);
                config.AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"Config/appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                //register ViewModels
                ConfigureViewModels(services);

                //register Views
                ConfigureViews(services);

                //register Services
                ConfigureServices(services);
            })
            .Build();

        await _host.StartAsync();

        //show the main window
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <summary>
    /// Application exit event handler. Setup cleanup of resources.
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }
        finally
        {
            base.OnExit(e);
        }
    }

    /// <summary>
    /// Registers all ViewModels in the dependency injection container.
    /// </summary>
    private static void ConfigureViewModels(IServiceCollection services)
    {
        //register ViewModels as Transient (creates a new instance each time)
        services.AddTransient<MainViewModel>();
        services.AddTransient<EnvironmentViewModel>();
        //The DockerConsoleViewModel depends on IContainerService, which has beem registered as a Singleton
        services.AddTransient<ContainerConsoleViewModel>();
    }

    /// <summary>
    /// Registers all Views (Windows/UserControls) in the dependency injection container.
    /// </summary>
    private static void ConfigureViews(IServiceCollection services)
    {
        //register Windows as Transient (creates a new instance each time)
        services.AddTransient<MainWindow>();
        
        //register UserControls as Transient
        services.AddTransient<ContainerConsole>();
    }

    /// <summary>
    /// Registers application services in the dependency injection container.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        //register container services
        services.AddSingleton<IContainerService, DockerService>();
        services.AddSingleton<IImageService>(provider => (IImageService)provider.GetRequiredService<IContainerService>());
        services.AddSingleton<IContainerFileTransferService, ContainerFileTransferService>();
        services.AddTransient<IIgnorePatternMatcher, IgnorePatternMatcher>();
        services.AddTransient<IFileSyncService, FileSyncService>();
        
        //register user control service
        services.AddSingleton<IUserControlService, UserControlService>();
        
        //register runners used by ContainerConsoleViewModel
        services.AddTransient<ILogRunner, LogRunner>();
        services.AddTransient<ICommandRunner, CommandRunner>();
        
        //register settings service
        services.AddSingleton<ISettingsService, SettingsService>();

        //register UI services
        services.AddSingleton<IDialogService, WPFDialogService>();
        services.AddSingleton<IViewLocator, ViewLocator>();
        services.AddSingleton<IClipboardService, WPFClipboardService>();

        // register external process service
        services.AddSingleton<IExternalProcessService, ExternalProcessService>();
        services.AddSingleton<ISyncIgnoreService, SyncIgnoreService>();

        // register environment service abstraction
        services.AddSingleton<IEnvironmentService, EnvironmentService>();

        // reading the container creation arguments from the file and creating a HostConfig from it
        string hostConfigStr = File.ReadAllText(Path.Combine("Config", "container_creation_args.json"));
        services.AddScoped<HostConfig>(_ => JsonConvert.DeserializeObject<HostConfig>(hostConfigStr)!);
    }
}