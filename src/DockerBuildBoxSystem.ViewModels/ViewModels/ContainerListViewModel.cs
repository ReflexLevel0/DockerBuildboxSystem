using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Docker.DotNet.Models;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public partial class ContainerListViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IImageService _imageService;
        private readonly IContainerService _containerService;
        private readonly IExternalProcessService _externalProcessService;
        private readonly IViewModelLogger _logger;

        private string ContainerId
        {
            get => SelectedContainer?.Id ?? string.Empty;
        }
        private bool IsContainerRunning => SelectedContainer?.IsRunning == true;

        [ObservableProperty]
        private ContainerObservableCollection<ImageInfo> _images = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshImagesCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestartContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(OpenContainerInCmdCommand))]
        private ContainerInfo? _selectedContainer;

        /// <summary>
        /// The selected image info object.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshImagesCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestartContainerCommand))]
        [NotifyCanExecuteChangedFor(nameof(OpenContainerInCmdCommand))]
        private ImageInfo? _selectedImage;

        /// <summary>
        /// If true, include intermediate containers in the list.
        /// </summary>
        [ObservableProperty]
        private bool _showAllImages = true;
        /// <summary>
        /// True while the image list is being refreshed.
        /// </summary>
        [ObservableProperty]
        private bool _isLoadingImages;

        [ObservableProperty]
        private bool _isSwitching;

        //track previous selected container and image id to manage stop-on-switch behavior
        private string? _previousContainerId;
        private string? _previousImageId;

        private readonly SemaphoreSlim _imageSwitchLock = new(1, 1);
        private CancellationTokenSource? _switchCts;

        private int _switchingCount;

        private readonly HashSet<string> _containersStartedByApp = new(StringComparer.OrdinalIgnoreCase);

        private int _disposeOnce;

        public bool AutoStartLogs { get; set; } = true;

        public ContainerListViewModel(
            IServiceProvider serviceProvider,
            IImageService imageService,
            IContainerService containerService,
            IExternalProcessService externalProcessService,
            IViewModelLogger logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
            _containerService = containerService ?? throw new ArgumentNullException(nameof(containerService));
            _externalProcessService = externalProcessService ?? throw new ArgumentNullException(nameof(externalProcessService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Invoked when the value of the "Show All Images" setting changes.
        /// </summary>
        /// <param name="value">The new value of the "Show All Images" setting.
        /// <see langword="true"/> if all images should be shown; otherwise, <see langword="false"/>.</param>
        partial void OnShowAllImagesChanged(bool value) => RefreshImagesCommand.ExecuteAsync(null);

        /// <summary>
        /// Invoked when the selected image changes.
        /// Triggers the async image selection handler.
        /// </summary>
        /// <param name="value">The newly selected image info or null.</param>
        partial void OnSelectedImageChanged(ImageInfo? value) => _ = OnSelectedImageChangedAsync(value);

        /// <summary>
        /// Refreshes the list of images from the image service.
        /// </summary>
        [RelayCommand]
        public async Task RefreshImagesAsync()
        {
            var selectedImageId = SelectedImage?.Id;
            IsLoadingImages = true;
            try
            {
                //not using ConfigureAwait(false) since we want to return to the UI thread as soon as possible (no stalling :))
                var images = await _imageService.ListImagesAsync(all: ShowAllImages);

                //Back to the UI threa so safe to update ObservableCollection
                Images.Clear();
                foreach (var image in images)
                {
                    if (string.Compare(image.Id, selectedImageId) == 0)
                    {
                        SelectedImage = image;
                    }
                    Images.Add(image);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[image-list-error] {ex.Message}", true, false);
            }
            finally
            {
                IsLoadingImages = false;
            }
        }

        /// <summary>
        /// Updates dependent state when the selected image changes.
        /// </summary>
        /// <param name="value">The newly selected image info or null.</param>
        public async Task OnSelectedImageChangedAsync(ImageInfo? value)
        {
            //if images are still loading and selection is reset, ignore.
            if (IsLoadingImages && value is null)
                return;

            var newImageId = value?.Id;
            if (newImageId == _previousImageId)
                return;

            _previousImageId = newImageId;

            Interlocked.Increment(ref _switchingCount);
            IsSwitching = true;

            //cancel any pending start operations from a previous selection
            try
            {
                _switchCts?.Cancel();
            }
            catch (ObjectDisposedException) { /* ignoring... */ }

            _switchCts?.Dispose();
            _switchCts = new CancellationTokenSource();
            var ct = _switchCts.Token;

            var takeLock = false;
            try
            {
                //make switch operation cancelable  to avoid releasing an unacquired lock.
                await _imageSwitchLock.WaitAsync(ct);
                takeLock = true;

                ct.ThrowIfCancellationRequested();

                var newImage = value;

                ct.ThrowIfCancellationRequested();

                if (newImage is null)
                {
                    SelectedImage = null;
                    _previousContainerId = ContainerId;
                    return;
                }

                var primaryTag = newImage.RepoTags.FirstOrDefault();
                var imageName = primaryTag ?? newImage.Id;

                _logger.LogWithNewline($"[info] Selected image: {imageName}", false, false);

                //try to find an existing container for this image.
                var containers = await _containerService.ListContainersAsync(all: true, ct: ct);

                var existingContainer = containers.FirstOrDefault(c =>
                    (!string.IsNullOrEmpty(primaryTag) && c.Image == primaryTag) || c.Image == newImage.Id);

                ContainerInfo container;
                if (existingContainer is not null)
                {
                    _logger.LogWithNewline(
                        $"[info] Found existing container: {existingContainer.Names.FirstOrDefault() ?? existingContainer.Id}",
                        false, false);

                    //ensure we get the latest state
                    container = await _containerService.InspectAsync(existingContainer.Id, ct);
                }
                else
                {
                    _logger.LogWithNewline("[info] No existing container found for image. Creating a new one...", false, false);

                    var newContainerId = await _containerService.CreateContainerAsync(
                        new ContainerCreationOptions
                        {
                            ImageName = imageName,
                            Config = (HostConfig)_serviceProvider.GetService(typeof(HostConfig))!
                        },
                        ct: ct);
                    container = await _containerService.InspectAsync(newContainerId, ct);

                    var createdName = container.Names.FirstOrDefault() ?? container.Id;
                    _logger.LogWithNewline($"[info] Created new container: {createdName}", false, false);
                }

                SelectedContainer = container;

                ct.ThrowIfCancellationRequested();

                //start the container
                await StartContainerInternalAsync(ct);

                _previousContainerId = ContainerId;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[selection-error] {ex.Message}", true, false);
            }
            finally
            {
                if (takeLock)
                    _imageSwitchLock.Release();

                if (Interlocked.Decrement(ref _switchingCount) == 0)
                    IsSwitching = false;
            }
        }

        public async Task RefreshSelectedContainerAsync()
        {
            if (string.IsNullOrWhiteSpace(ContainerId)) return;
            try
            {
                SelectedContainer = await _containerService.InspectAsync(ContainerId);
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[refresh-error] {ex.Message}", true, false);
            }
        }
        private bool CanStartContainer() => !string.IsNullOrWhiteSpace(ContainerId) && !IsContainerRunning;
        
        /// <summary>
        /// Starts the selected container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartContainer))]
        public async Task StartContainerAsync()
        {
            await _imageSwitchLock.WaitAsync();
            try
            {
                await StartContainerInternalAsync(CancellationToken.None);
            }
            finally
            {
                _imageSwitchLock.Release();
            }
        }

        private async Task StartContainerInternalAsync(CancellationToken ct)
        {
            if (SelectedContainer is null) return;

            if (ct.IsCancellationRequested) return;

            var name = SelectedContainer.Names.FirstOrDefault() ?? SelectedContainer.Id;
            try
            {
                _logger.LogWithNewline($"[info] Starting container: {name}", false, false);

                var status = await _containerService.StartAsync(ContainerId, ct);

                if (ct.IsCancellationRequested)
                {
                    _logger.LogWithNewline($"[info] Startup cancelled for: {name}", false, false);
                    return;
                }

                if (status)
                {
                    if (!string.IsNullOrWhiteSpace(SelectedContainer?.Id))
                        _containersStartedByApp.Add(SelectedContainer.Id);

                    _logger.LogWithNewline($"[info] Started container: {name}", false, false);
                    
                    // Re-inspect to get the updated running state
                    SelectedContainer = await _containerService.InspectAsync(ContainerId, ct);
                }
                else
                {
                    _logger.LogWithNewline($"[start-container] Container did not start: {name}", true, false);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWithNewline($"[info] Operation cancelled: {name}", false, false);
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[start-container-error] {ex.Message}", true, false);
            }
        }

        private bool CanStopContainer() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning;

        /// <summary>
        /// Stops a running container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopContainer))]
        public async Task StopContainerAsync() => await StopContainerByIdAsync(SelectedContainer);

        /// <summary>
        /// Stops a container by id (used when auto-stopping previous selection).
        /// </summary>
        private async Task StopContainerByIdAsync(ContainerInfo? container)
        {
            if (container is null) return;

            try
            {
                var name = container.Names.FirstOrDefault() ?? container.Id;
                _logger.LogWithNewline($"[info] Stopping container: {name}", false, false);
                await _containerService.StopAsync(container.Id, timeout: TimeSpan.FromSeconds(10));
                _logger.LogWithNewline($"[info] Stopped container: {name}", false, false);
                
                // Re-inspect to get the updated state if this is the selected container
                if (SelectedContainer?.Id == container.Id)
                {
                    SelectedContainer = await _containerService.InspectAsync(container.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[stop-container-error] {ex.Message}", true, false);
            }
        }

        private bool CanRestartContainer() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning;

        /// <summary>
        /// Restarts a running container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRestartContainer))]
        public async Task RestartContainerAsync()
        {
            if (string.IsNullOrWhiteSpace(ContainerId)) return;
            try
            {
                _logger.LogWithNewline($"[info] Restarting container: {ContainerId}", false, false);
                await _containerService.RestartAsync(ContainerId, timeout: TimeSpan.FromSeconds(10));
                _logger.LogWithNewline($"[info] Restarted container: {ContainerId}", false, false);
                
                // Re-inspect to get the updated state
                SelectedContainer = await _containerService.InspectAsync(ContainerId);
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[restart-container-error] {ex.Message}", true, false);
            }
        }

        /// <summary>
        /// Open the selected container in a Windows command prompt.
        /// </summary>
        /// <remarks>This command opens a new command prompt window with the specified container's shell.
        /// The container must be running for this command to succeed. It syncs with the currently selected container.
        /// </remarks>
        /// <returns>the task representing the asynchronous operation.</returns>
        [RelayCommand(CanExecute = nameof(IsContainerRunning))]
        public async Task OpenContainerInCmdAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedContainer?.Id)) return;
            try
            {
                _logger.LogWithNewline($"[info] Opening container in windows cmd: {SelectedContainer.Id}", false, false);
                _externalProcessService.StartProcess("cmd.exe", $"/K docker exec -it {SelectedContainer.Id} bash");
            }
            catch (Exception ex)
            {
                _logger.LogWithNewline($"[open-shell-error] {ex.Message}", true, false);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeOnce, 1) != 0)
                return;

            try
            {
                _switchCts?.Cancel();
            }
            catch (ObjectDisposedException) { }
            finally
            {
                _switchCts?.Dispose();
                _imageSwitchLock?.Dispose();
            }

            // Stop container(s) on app exit (container-based workflow).
            // - Always stop the currently selected running container.
            // - Also stop any containers started by this app instance (optional tracking).
            try
            {
                var idsToStop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(SelectedContainer?.Id))
                    idsToStop.Add(SelectedContainer.Id);

                foreach (var id in _containersStartedByApp)
                    idsToStop.Add(id);

                foreach (var id in idsToStop)
                {
                    try
                    {
                        var info = await _containerService.InspectAsync(id);
                        if (info.IsRunning)
                        {
                            var name = info.Names.FirstOrDefault() ?? info.Id;
                            await _containerService.StopAsync(id, timeout: TimeSpan.FromSeconds(10));
                        }
                    }
                    catch
                    {
                        // best-effort shutdown; ignore failures
                    }
                }

                _containersStartedByApp.Clear();
            }
            catch
            {
                // ignore shutdown failures
            }

            await base.DisposeAsync();
        }
    }
}
