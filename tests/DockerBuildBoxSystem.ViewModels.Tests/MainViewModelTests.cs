using DockerBuildBoxSystem.ViewModels.Main;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace DockerBuildBoxSystem.ViewModels.Tests;

public class MainViewModelTests
{
    [Fact]
    public void Title_From_Config_Name_And_Version()
    {
        var cfg = Substitute.For<IConfiguration>();
        cfg["Application:Name"].Returns("CoolApp");
        cfg["Application:Version"].Returns("1.2.3");

        var vm = new MainViewModel(cfg);

        Assert.Equal("CoolApp v1.2.3", vm.Title);
    }

    [Fact]
    public void Title_Defaults_To_Name_When_Version_Missing()
    {
        var cfg = Substitute.For<IConfiguration>();
        cfg["Application:Name"].Returns("CoolApp");
        cfg["Application:Version"].Returns((string?)null);

        var vm = new MainViewModel(cfg);

        Assert.Equal("CoolApp", vm.Title);
    }

    [Fact]
    public async Task ExitCommand_Raises_ExitRequested()
    {
        var cfg = Substitute.For<IConfiguration>();
        var vm = new MainViewModel(cfg);

        var raised = false;
        vm.ExitRequested += (_, _) => raised = true;

        await vm.ExitCommand.ExecuteAsync(null);

        Assert.True(raised);
    }
}
