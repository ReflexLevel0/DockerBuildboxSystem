using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Docker.DotNet.Models;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using CommunityToolkit.Mvvm.Messaging;
using DockerBuildBoxSystem.ViewModels.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{

    /// <summary>
    /// ViewModel for a container console that streams logs and executes commands inside Docker containers.
    /// </summary>
    public sealed partial class ContainerConsoleViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IClipboardService? _clipboard;
        private readonly IViewModelLogger logger;
        private SynchronizationContext? _synchronizationContext;

        public readonly UILineBuffer UIHandler;

        /// <summary>
        /// Lines currently displayed in the console UI.
        /// </summary>
        public RangeObservableCollection<ConsoleLine> Lines { get; } = new RangeObservableCollection<ConsoleLine>();

        // Sub-ViewModels
        public ContainerListViewModel ContainerList { get; }
        public LogStreamViewModel Logs { get; }
        public FileSyncViewModel FileSync { get; }
        public UserControlsViewModel UserControls { get; }
        public CommandExecutionViewModel Commands { get; }


        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerConsoleViewModel"/> class.
        /// </summary>
        /// <param name="serviceProvider">The service provider used to resolve application-wide services and configuration objects. Cannot be <see
        /// langword="null"/>.</param>
        /// <param name="imageService">The service responsible for managing container images. Cannot be <see langword="null"/>.</param>
        /// <param name="containerService">The service used for container lifecycle operations and queries. Cannot be <see langword="null"/>.</param>
        /// <param name="fileSyncService">The service that handles file synchronization between the host and containers. Cannot be <see
        /// langword="null"/>.</param>
        /// <param name="configuration">The application configuration provider. Cannot be <see langword="null"/>.</param>
        /// <param name="settingsService">The service for accessing and persisting user or application settings. Cannot be <see langword="null"/>.</param>
        /// <param name="userControlService">The service managing user-defined controls or shortcuts. Cannot be <see langword="null"/>.</param>
        /// <param name="logRunner">The service responsible for running and streaming container logs. Cannot be <see langword="null"/>.</param>
        /// <param name="cmdRunner">The service used to execute commands within containers. Cannot be <see langword="null"/>.</param>
        /// <param name="externalProcessService">The service for launching and managing external processes. Cannot be <see langword="null"/>.</param>
        /// <param name="clipboard">An optional clipboard service for copy and paste operations. May be <see langword="null"/> if clipboard functionality is not required.</param>
        public ContainerConsoleViewModel(
            IServiceProvider serviceProvider,
            IImageService imageService,
            IContainerService containerService,
            IFileSyncService fileSyncService,
            IConfiguration configuration,
            ISettingsService settingsService,
            IUserControlService userControlService,
            ILogRunner logRunner,
            ICommandRunner cmdRunner,
            IExternalProcessService externalProcessService,
            IClipboardService? clipboard = null) : base()
        {
            _serviceProvider = serviceProvider;
            _clipboard = clipboard;

            UIHandler = new UILineBuffer(Lines);
            
            logger = new ViewModelLogger(UIHandler);

            //initialize sub-viewModels
            ContainerList = new ContainerListViewModel(serviceProvider, imageService, containerService, externalProcessService, logger);
            Logs = new LogStreamViewModel(logRunner, containerService, logger);
            FileSync = new FileSyncViewModel(fileSyncService, settingsService, logger);
            UserControls = new UserControlsViewModel(userControlService, logger);
            Commands = new CommandExecutionViewModel(cmdRunner, containerService, userControlService, logger, UserControls);


    FileSync.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FileSync.IsSyncRunning))
                {
                    Commands.IsSyncRunning = FileSync.IsSyncRunning;
                }
            };

                        
            //send initial AutoStartLogs value to ensure the image list is synchronized
            WeakReferenceMessenger.Default.Send(new AutoStartLogsChangedMessage(Logs.AutoStartLogs));

            // Periodically refreshing container and image info 
            var refreshImagesContainersTimer = new System.Timers.Timer(new TimeSpan(0, 0, 5));
            refreshImagesContainersTimer.Elapsed += async (_, _) =>
            {
                if (_synchronizationContext != null)
                {
                    _synchronizationContext.Post(async _ =>
                    {
                        await ContainerList.RefreshImagesCommand.ExecuteAsync(null);
                        await ContainerList.RefreshSelectedContainerAsync();
                    }, null);
                }
            };
            refreshImagesContainersTimer.Enabled = true;
        }



        /// <summary>
        /// Initializes the ViewModel: 
        ///     starts the UI update loop, 
        ///     refreshes images, 
        ///     loads user controls
        /// </summary>
        [RelayCommand]
        private async Task InitializeAsync()
        {
            // Start the global UI update task
            UIHandler.Start();

            // Load available images on initialization
            await ContainerList.RefreshImagesCommand.ExecuteAsync(null);

            // Load user-defined controls
            await UserControls.LoadUserControlsAsync();
        }



        /// <summary>
        /// Clears all lines from the console.
        /// </summary>
        [RelayCommand]
        private void Clear()
        {
            UIHandler.Clear();
        }

        /// <summary>
        /// The copy command to copy output of the container to clipboard.
        /// </summary>
        [RelayCommand]
        private async Task CopyAsync()
        {
            if (_clipboard is null)
                return;

            await UIHandler.CopyAsync(_clipboard);
        }

        /// <summary>
        /// cancel and cleanup task
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            await ContainerList.DisposeAsync();
            await Logs.DisposeAsync();
            await FileSync.DisposeAsync();
            await Commands.DisposeAsync();

            await UIHandler.StopAsync();
            await base.DisposeAsync();
        }

        public void SetSynchronizationContext(SynchronizationContext? context)
        {
            _synchronizationContext = context;
        }
    }
}
