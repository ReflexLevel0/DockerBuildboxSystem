using System.Threading.Channels;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.Models;
using DockerBuildBoxSystem.ViewModels.ViewModels;
using DockerBuildBoxSystem.ViewModels.Common;
using NSubstitute;
using static DockerBuildBoxSystem.TestUtils.ChannelTestUtil;

namespace DockerBuildBoxSystem.ViewModels.Tests;

public class ContainerConsoleViewModelTests
{
    private static ContainerConsoleViewModel CreateViewModel(
        IContainerService? service = null,
        IClipboardService? clipboard = null)
    {
        service ??= Substitute.For<IContainerService>();
        clipboard ??= Substitute.For<IClipboardService>();

        return new ContainerConsoleViewModel(service, clipboard);
    }

    /// <summary>
    /// Verifies that the initialization process correctly loads containers and user commands.
    /// </summary>
    /// <remarks>This test ensures that the <c>InitializeCommand</c> properly retrieves a list of running
    /// containers and populates the <c>Containers</c> and <c>UserCommands</c> collections in the view model.</remarks>
    [Fact]
    public async Task Initialize_Loads_Containers_And_UserCommands()
    {
        //Arrange
        var ContainerService = Substitute.For<IContainerService>();

        //The ListContainersAsync method is subbed to return a list with one running container
        ContainerService
            .ListContainersAsync(true, null, default)
            .Returns(Task.FromResult<IList<ContainerInfo>>(new List<ContainerInfo>
            {
                new ContainerInfo { Id = "1", Names = new []{"n1"}, State = "running" }
            }));

        var vm = CreateViewModel(ContainerService);

        //Act
        await vm.InitializeCommand.ExecuteAsync(null);

        //Assert
        Assert.Single(vm.Containers);
        //Creating default user commands 
        Assert.NotEmpty(vm.UserCommands);
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

        //The InspectAsync method is stubbed to return a container
        ContainerService
            .InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new ContainerInfo { Id = ci.ArgAt<string>(0), Names = ["n"], Tty = false }));

        ContainerService
            .StreamLogsAsync(Arg.Any<string>(), true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(CreateCompletedReader([(false, "sup")])));

        var vm = CreateViewModel(ContainerService);
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.AutoStartLogs = true;
        vm.SelectedContainer = new ContainerInfo { Id = "abc", Names = ["abc"] };

        //Act
        //Wait until line appears., with timeout after 2 seconds...
        var ok = await WaitUntilAsync(() => vm.Lines.Any(l => l.Text == "sup"), TimeSpan.FromSeconds(2));

        //Assert
        Assert.True(ok);
        Assert.True(vm.IsLogsRunning);
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
        ContainerService.StreamExecAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((output, (Task<long>)exitTask)));

        var vm = CreateViewModel(ContainerService);

        //start the UI update loop
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedContainer = new ContainerInfo { Id = "abc", Names = ["abc"] };
        //Example command to send
        vm.Input = "echo hi";

        //Act
        await vm.SendCommand.ExecuteAsync(null);

        //Assert
        var ok = await WaitUntilAsync(() => vm.Lines.Any(l => l.Text.Contains("[exit] 0")), TimeSpan.FromSeconds(2));
        Assert.True(ok);
        Assert.Contains(vm.Lines, l => l.Text == "line1");
        Assert.Contains(vm.Lines, l => l.Text == "err1");
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

        //The InspectAsync method is stubbed to return a container
        ContainerService.InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContainerInfo { Id = "abc", Names = ["abc"], Tty = false }));

        //create a reader that ONLY completes when cancelled
        var reader = CreateCancellableReader([(false, "start")]);
        ContainerService.StreamLogsAsync(Arg.Any<string>(), true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(reader));

        var vm = CreateViewModel(ContainerService);
        //start the UI update loop
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.ContainerId = "abc";

        //Act & Assert
        await vm.StartLogsCommand.ExecuteAsync(null);

        Assert.True(vm.IsLogsRunning == true);

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
        var Clipboard = Substitute.For<IClipboardService>();

        var vm = CreateViewModel(ContainerService, Clipboard);
        vm.Lines.Add(new ConsoleLine(DateTime.Now, "sup", false));

        //Act
        await vm.CopyCommand.ExecuteAsync(null);

        //Assert
        await Clipboard.Received().SetTextAsync(Arg.Is<string>(s => s.Contains("sup")), Arg.Any<CancellationToken>());
    }
}
