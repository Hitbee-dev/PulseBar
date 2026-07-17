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
            ["-d", "Ubuntu-22.04", "--", "/home/user/.local/bin/codex", "app-server"],
            startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void BuildStartInfo_WslWithoutExplicitPath_UsesCodexFromPath()
    {
        var profile = new ProviderProfile(
            "codex-wsl", "codex", ExecutionEnvironmentType.Wsl, null, "Ubuntu-22.04", null, true);

        var startInfo = AppServerProcess.BuildStartInfo(profile);

        Assert.Contains("codex", startInfo.ArgumentList);
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
