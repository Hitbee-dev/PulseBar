using PulseBar.Core.Models;
using PulseBar.Providers.Codex.AppServer;

namespace PulseBar.Codex.Tests;

public class AppServerProcessTests
{
    [Fact]
    public void BuildStartInfo_WslProfile_LaunchesThroughWslExe()
    {
        var profile = new ProviderProfile(
            "codex-wsl", "codex", ExecutionEnvironmentType.Wsl,
            "/home/user/.local/bin/codex", "Ubuntu-22.04", null, true);

        var startInfo = AppServerProcess.BuildStartInfo(profile);

        Assert.Equal("wsl.exe", startInfo.FileName);
        Assert.Equal(
            [
                "-d", "Ubuntu-22.04", "--", "/usr/bin/env",
                "PATH=/home/user/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/snap/bin",
                "/home/user/.local/bin/codex", "app-server",
            ],
            startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void BuildStartInfo_WslWithoutExplicitPath_RunsBareCommandWithoutEnv()
    {
        var profile = new ProviderProfile(
            "codex-wsl", "codex", ExecutionEnvironmentType.Wsl, null, "Ubuntu-22.04", null, true);

        var startInfo = AppServerProcess.BuildStartInfo(profile);

        Assert.Equal(["-d", "Ubuntu-22.04", "--", "codex", "app-server"], startInfo.ArgumentList);
    }

    [Fact]
    public void BuildWslPathVariable_PrependsExecutableDirectory()
    {
        var pathVariable = AppServerProcess.BuildWslPathVariable(
            "/home/u/.nvm/versions/node/v22.15.0/bin/codex");

        Assert.StartsWith("PATH=/home/u/.nvm/versions/node/v22.15.0/bin:", pathVariable);
        Assert.Contains(":/usr/bin:", pathVariable);
    }

    [Fact]
    public void BuildWslPathVariable_BareCommand_ReturnsNull()
    {
        Assert.Null(AppServerProcess.BuildWslPathVariable("codex"));
    }

    [Fact]
    public void BuildStartInfo_WindowsNative_RunsExecutableDirectly()
    {
        var profile = new ProviderProfile(
            "codex-win", "codex", ExecutionEnvironmentType.WindowsNative,
            @"C:\tools\codex.exe", null, null, true);

        var startInfo = AppServerProcess.BuildStartInfo(profile);

        Assert.Equal(@"C:\tools\codex.exe", startInfo.FileName);
        Assert.Equal(["app-server"], startInfo.ArgumentList);
    }
}
