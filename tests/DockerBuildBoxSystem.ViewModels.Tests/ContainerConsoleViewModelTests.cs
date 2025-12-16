using System.Threading.Channels;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.ViewModels;
using DockerBuildBoxSystem.ViewModels.Common;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using static DockerBuildBoxSystem.TestUtils.ChannelTestUtil;
using Docker.DotNet.Models;

namespace DockerBuildBoxSystem.ViewModels.Tests;

public class ContainerConsoleViewModelTests
{
    private static ContainerConsoleViewModel CreateViewModel(
        IServiceProvider? serviceProvider = null,
        IContainerService? service = null,
        IImageService? imageService = null,
        IFileSyncService? fileSyncService = null,
        IConfiguration? configuration = null,
        ISettingsService? settingsService = null,
        IUserControlService? userControlService = null,
        ILogRunner? logRunner = null,
        ICommandRunner? commandRunner = null,
        IClipboardService? clipboard = null,
        HostConfig? hostConfig = null,
        IExternalProcessService? externalProcessService = null
        )
    {
        serviceProvider ??= Substitute.For<IServiceProvider>();
        service ??= Substitute.For<IContainerService>();
        imageService ??= Substitute.For<IImageService>();
        fileSyncService ??= Substitute.For<IFileSyncService>();
        fileSyncService.Changes.Returns(new System.Collections.ObjectModel.ObservableCollection<string>());
        configuration ??= Substitute.For<IConfiguration>();
        settingsService ??= Substitute.For<ISettingsService>();
        userControlService ??= Substitute.For<IUserControlService>();
        logRunner ??= Substitute.For<ILogRunner>();
        commandRunner ??= Substitute.For<ICommandRunner>();
        clipboard ??= Substitute.For<IClipboardService>();
        externalProcessService ??= Substitute.For<IExternalProcessService>();

        return new ContainerConsoleViewModel(serviceProvider, service, imageService, fileSyncService, configuration, settingsService, userControlService, logRunner, commandRunner, externalProcessService, clipboard);
    }

    /// <summary>
    /// Verifies that the initialization process correctly loads containers and user commands.
    /// </summary>
    /// <remarks>This test ensures that the <c>InitializeCommand</c> properly retrieves a list of running
    /// containers and populates the <c>Containers</c> and <c>UserCommands</c> collections in the view model.</remarks>
    [Fact]
    public async Task Initialize_Loads_Images_And_UserCommands()
    {
        //Arrange
        var imageService = Substitute.For<IImageService>();

        //The ListImagesAsync method is subbed to return a list with one image
        imageService
            .ListImagesAsync(true, default)
            .Returns(Task.FromResult<IList<ImageInfo>>(new List<ImageInfo>
            {
                new ImageInfo { Id = "1", RepoTags = new []{"n1"} }
            }));

        var vm = CreateViewModel(imageService: imageService);

        //Act
        await vm.InitializeCommand.ExecuteAsync(null);

        //Assert
        Assert.Single(vm.Images);
    }

    /// <summary>
    /// Verifies that selecting a container sets the container ID and automatically starts streaming logs IF auto-start
    /// is enabled.
    /// </summary>
    /// <remarks>This test ensures that when a container is selected, the container ID is updated, and log
    /// streaming begins automatically if the <see cref="AutoStartLogs"/> property is set to <see langword="true"/>. It
    /// also validates that the log streaming produces the expected output and updates the <see cref="IsLogsRunning"/>
    /// property.</remarks>
    [Fact]
    public async Task SelectedContainer_Sets_ContainerId_And_AutoStarts_Logs()
    {
        //Arrange
        var ContainerService = Substitute.For<IContainerService>();
        var LogsService = Substitute.For<ILogRunner>();

        //The InspectAsync method is stubbed to return a container
        ContainerService
            .InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new ContainerInfo { Id = ci.ArgAt<string>(0), Names = ["n"], Tty = false }));

        // Use a cancellable reader that doesn't complete immediately, so we can verify IsLogsRunning is true
        ContainerService
            .StreamLogsAsync(Arg.Any<string>(), true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(CreateCancellableReader([(false, "sup")])));


        LogsService
            .RunAsync(ContainerService, Arg.Any<string>())
            .Returns(ci =>
            {
                async IAsyncEnumerable<(bool IsStdErr, string Line)> Stream()
                {
                    LogsService.IsRunning.Returns(true);
                    yield return (false, "sup");
                }
                return Stream();
            });

        LogsService.StopAsync()
            .Returns(ci =>
            {
                LogsService.IsRunning.Returns(false);
                return Task.CompletedTask;
            });

        var vm = CreateViewModel(service: ContainerService, logRunner: LogsService);

        vm.AutoStartLogs = true;
        vm.SelectedContainer = new ContainerInfo { Id = "abc", Names = ["abc"] };

        await vm.InitializeCommand.ExecuteAsync(null);


        //Act
        //Wait until line appears., with timeout after 2 seconds...
        var logsStarted = await WaitUntilAsync(() => vm.IsLogsRunning, TimeSpan.FromSeconds(2));
        Assert.True(logsStarted, "Logs should have started!");

        //wait until line appears, with timeout after 2 seconds...
        var ok = await WaitUntilAsync(() => vm.UIHandler.Output.Contains("sup"), TimeSpan.FromSeconds(2));

        //Assert
        Assert.True(ok, "Line 'sup' should have appeared!");
        Assert.True(vm.IsLogsRunning, "Logs should still be running!");
     
        //cleanup
        await vm.StopLogsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Tests that the <c>SendCommand</c> method executes a command on the selected container, appends the output and
    /// error lines to the UI, and displays the exit code upon completionn.
    /// </summary>
    /// <remarks>This test verifies the behavior of the <c>SendCommand</c> method in the following scenarios:
    /// <list type="bullet"> 
    ///     <item>Ensures the command is executed on the selected container using the
    ///         <c>StreamExecAsync</c> method of the container service.</item>
    ///     <item>Validates that both standard output and error lines are appended to the <c>Lines</c> 
    ///         collection in the UI.</item>
    ///     <item>Confirms that the exit code is displayed in the UI AFTER the command completesm.</item> 
    ///     <item>Ensures the <c>IsCommandRunning</c> property is
    ///         set to <c>false</c> after the command execution finishes.</item> 
    /// </list>
    /// </remarks>
    [Fact]
    public async Task Send_Executes_Command_And_Appends_Output_And_Exit()
    {
        //Arrange
        var ContainerService = Substitute.For<IContainerService>();
        var CommandService = Substitute.For<ICommandRunner>();

        //The InspectAsync method is stubbed to return a container
        ContainerService
            .InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContainerInfo { Id = "abc", Names = ["abc"], Tty = false }));

        //The StreamExecAsync method is stubbed to return a reader with some predefined output lines and an exit code task
        var output = CreateCompletedReader(
        [
            (false, "line1"),
            (true,  "err1"),
        ]);
        var exitTask = Task.FromResult(0L);
        var inputWriter = Channel.CreateUnbounded<string>().Writer;
        ContainerService.StreamExecAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((output, inputWriter, (Task<long>)exitTask)));

        CommandService
            .RunAsync(ContainerService, Arg.Any<string>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                async IAsyncEnumerable<(bool IsStdErr, string Line)> Stream()
                {
                    CommandService.IsRunning.Returns(true);
                    await foreach (var (isErr, line) in output.ReadAllAsync().ConfigureAwait(false))
                    {
                        yield return (isErr, line);
                    }
                    CommandService.IsRunning.Returns(false);
                }
                return Stream();
            });

        CommandService.ExitCode.Returns(exitTask);

        CommandService.StopAsync()
            .Returns(ci =>
            {
                CommandService.IsRunning.Returns(false);
                return Task.CompletedTask;
            });

        var vm = CreateViewModel(service: ContainerService, commandRunner: CommandService);

        //start the UI update loop
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedContainer = new ContainerInfo { Id = "abc", Names = ["abc"] };
        //Example command to send
        vm.Input = "echo hi";

        //Act
        await vm.SendCommand.ExecuteAsync(null);

        //Assert
        var ok = await WaitUntilAsync(() => vm.UIHandler.Output.Contains("[exit] 0"), TimeSpan.FromSeconds(2));
        Assert.True(ok);
        Assert.Contains("line1", vm.UIHandler.Output);
        Assert.Contains("err1", vm.UIHandler.Output);
        Assert.False(vm.IsCommandRunning);
    }

    /// <summary>
    /// Verifies that the <c>StopLogsCommand</c> actually cancels the log streaming operation and updates the
    /// <c>IsLogsRunning</c> property to <see langword="false"/>.
    /// </summary>
    /// <remarks>The test ensures that when the <c>StopLogsCommand</c> is executed, the log streaming
    /// operation is properly canceled, and the <c>IsLogsRunning</c> property reflects the stopped state. It also
    /// validates that the cancellation occurs within a reasonable time frame.</remarks>
    [Fact]
    public async Task StopLogs_Cancels_Log_Stream()
    {
        //Arrange
        var ContainerService = Substitute.For<IContainerService>();
        var LogsService = Substitute.For<ILogRunner>();

        //The InspectAsync method is stubbed to return a container
        ContainerService.InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContainerInfo { Id = "abc", Names = ["abc"], Tty = false }));

        LogsService
            .RunAsync(ContainerService, Arg.Any<string>())
            .Returns(ci =>
            {
                async IAsyncEnumerable<(bool IsStdErr, string Line)> Stream()
                {
                    LogsService.IsRunning.Returns(true);
                    yield return (false, "sup");
                }
                return Stream();
            });

        LogsService.StopAsync()
            .Returns(ci =>
            {
                LogsService.IsRunning.Returns(false);
                return Task.CompletedTask;
            });

        //create a reader that ONLY completes when cancelled
        var reader = CreateCancellableReader([(false, "start")]);
        ContainerService.StreamLogsAsync(Arg.Any<string>(), true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(reader));

        var vm = CreateViewModel(service: ContainerService, logRunner: LogsService);
        //start the UI update loop
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedContainer = new ContainerInfo { Id = "abc", Names = ["abc"] };

        //Act & Assert
        _ = vm.StartLogsCommand.ExecuteAsync(null);

        //wait for logs to actually start streaming
        var logsStarted = await WaitUntilAsync(() => vm.IsLogsRunning == true, TimeSpan.FromSeconds(2));
        Assert.True(logsStarted, "Logs should have started");

        await vm.StopLogsCommand.ExecuteAsync(null);

        var ok = await WaitUntilAsync(() => vm.IsLogsRunning == false, TimeSpan.FromSeconds(2));
        Assert.True(ok);
    }

    /// <summary>
    /// Verifies that the copy operation uses the clipboard service to copy text.
    /// </summary>
    /// <remarks>This test ensures that the <see cref="IClipboardService"/> is correctly invoked with th expected text 
    /// when the copy command is executed. It validates that the clipboard service receives the correct content from
    /// the view model's lines.</remarks>
    [Fact]
    public async Task Copy_Uses_Clipboard_Service()
    {
        //Arrange
        var ContainerService = Substitute.For<IContainerService>();
        var LogsService = Substitute.For<ILogRunner>();
        var Clipboard = Substitute.For<IClipboardService>();

        var vm = CreateViewModel(service: ContainerService, clipboard: Clipboard, logRunner: LogsService);

        ContainerService
            .InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContainerInfo { Id = "abc", Names = ["abc"], Tty = false }));

        ContainerService
            .StreamLogsAsync(Arg.Any<string>(), true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateCompletedReader([(false, "sup")])));

        LogsService
            .RunAsync(ContainerService, Arg.Any<string>())
            .Returns(ci =>
            {
                async IAsyncEnumerable<(bool IsStdErr, string Line)> Stream()
                {
                    LogsService.IsRunning.Returns(true);
                    yield return (false, "sup");
                    LogsService.IsRunning.Returns(false);
                }
                return Stream();
            });

        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedContainer = new ContainerInfo { Id = "abc", Names = ["abc"] };
        await vm.StartLogsCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => vm.UIHandler.Output.Contains("sup"), TimeSpan.FromSeconds(2));

        //Act
        await vm.CopyCommand.ExecuteAsync(null);

        //Assert
        await Clipboard.Received().SetTextAsync(Arg.Is<string>(s => s.Contains("sup")), Arg.Any<CancellationToken>());
    }
}
