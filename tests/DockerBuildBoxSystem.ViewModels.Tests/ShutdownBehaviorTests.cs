using System;
using System.Threading;
using System.Threading.Tasks;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.ViewModels;
using NSubstitute;
using Xunit;

namespace DockerBuildBoxSystem.ViewModels.Tests
{
    public class ShutdownBehaviorTests
    {
        private static ContainerConsoleViewModel CreateViewModel(
            IContainerService? containerService = null,
            IImageService? imageService = null,
            IDialogService? dialogService = null,
            IFileSyncService? fileSyncService = null,
            Microsoft.Extensions.Configuration.IConfiguration? configuration = null,
            ISettingsService? settingsService = null,
            IUserControlService? userControlService = null,
            ILogRunner? logRunner = null,
            ICommandRunner? commandRunner = null,
            IClipboardService? clipboard = null,
            IExternalProcessService? externalProcessService = null)
        {
            containerService ??= Substitute.For<IContainerService>();
            imageService ??= Substitute.For<IImageService>();
            dialogService ??= Substitute.For<IDialogService>();
            fileSyncService ??= Substitute.For<IFileSyncService>();
            configuration ??= Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>();
            settingsService ??= Substitute.For<ISettingsService>();
            userControlService ??= Substitute.For<IUserControlService>();
            logRunner ??= Substitute.For<ILogRunner>();
            commandRunner ??= Substitute.For<ICommandRunner>();
            clipboard ??= Substitute.For<IClipboardService>();
            externalProcessService ??= Substitute.For<IExternalProcessService>();

            // initialize file sync changes collection to avoid nulls inside VM
            fileSyncService.Changes.Returns(new System.Collections.ObjectModel.ObservableCollection<string>());

            return new ContainerConsoleViewModel(
                Substitute.For<IServiceProvider>(),
                imageService,
                containerService,
                dialogService,
                fileSyncService,
                configuration,
                settingsService,
                userControlService,
                logRunner,
                commandRunner,
                externalProcessService,
                clipboard);
        }

        [Fact]
        public async Task Dispose_Stops_Selected_Running_Container()
        {
            var containerService = Substitute.For<IContainerService>();
            var vm = CreateViewModel(containerService: containerService);

            // Selected container marked as running
            var selected = new ContainerInfo
            {
                Id = "abc",
                Names = new[] { "abc" },
                Status = "running",
                Tty = false
            };
            vm.ContainerList.SelectedContainer = selected;

            // Inspect returns same running info
            containerService.InspectAsync("abc", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(selected));

            await vm.DisposeAsync();

            await containerService.Received(1)
                .StopAsync("abc", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Dispose_Skips_Stop_When_Not_Running()
        {
            var containerService = Substitute.For<IContainerService>();
            var vm = CreateViewModel(containerService: containerService);

            var selected = new ContainerInfo
            {
                Id = "xyz",
                Names = new[] { "xyz" },
                Status = "exited",
                Tty = false
            };
            vm.ContainerList.SelectedContainer = selected;

            containerService.InspectAsync("xyz", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(selected));

            await vm.DisposeAsync();

            await containerService.DidNotReceiveWithAnyArgs()
                .StopAsync((string)default!, default(TimeSpan), default(CancellationToken));
        }

        [Fact]
        public async Task Dispose_Safe_When_No_Selected_Container()
        {
            var containerService = Substitute.For<IContainerService>();
            var vm = CreateViewModel(containerService: containerService);

            vm.ContainerList.SelectedContainer = null;
            await vm.DisposeAsync();

            await containerService.DidNotReceiveWithAnyArgs()
                .StopAsync((string)default!, default(TimeSpan), default(CancellationToken));
        }
    }
}
