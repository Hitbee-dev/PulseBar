using System.Text.Json.Nodes;
using PulseBar.Providers.Claude.Statusline;

namespace PulseBar.Claude.Tests;

public sealed class ClaudeSettingsInstallerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _settingsPath;
    private readonly string _backupDir;

    public ClaudeSettingsInstallerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PulseBarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settingsPath = Path.Combine(_dir, "settings.json");
        _backupDir = Path.Combine(_dir, "backups");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string BridgeCommand =
        "\"C:\\Program Files\\PulseBar\\PulseBar.Bridge.exe\" claude-statusline --output \"C:\\x\\claude-status.json\"";

    [Fact]
    public void Install_NoSettingsFile_CreatesOneWithStatusLine()
    {
        var result = ClaudeSettingsInstaller.Install(_settingsPath, BridgeCommand, _backupDir);

        Assert.Equal(StatuslineInstallResult.Installed, result);
        var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!;
        Assert.Equal("command", root["statusLine"]!["type"]!.GetValue<string>());
        Assert.Equal(BridgeCommand, root["statusLine"]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Install_NoStatusLine_AddsAndBacksUpAndPreservesOtherFields()
    {
        File.WriteAllText(_settingsPath, """{"model":"opusplan","env":{"FOO":"1"}}""");

        var result = ClaudeSettingsInstaller.Install(_settingsPath, BridgeCommand, _backupDir);

        Assert.Equal(StatuslineInstallResult.Installed, result);
        var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!;
        Assert.Equal("opusplan", root["model"]!.GetValue<string>());
        Assert.Equal("1", root["env"]!["FOO"]!.GetValue<string>());
        Assert.NotNull(root["statusLine"]);
        Assert.Single(Directory.GetFiles(_backupDir, "claude-settings.*.json"));
    }

    [Fact]
    public void Install_ExistingForeignStatusLine_IsNeverTouched()
    {
        var original = """{"statusLine":{"type":"command","command":"node hud.mjs"}}""";
        File.WriteAllText(_settingsPath, original);

        var result = ClaudeSettingsInstaller.Install(_settingsPath, BridgeCommand, _backupDir);

        Assert.Equal(StatuslineInstallResult.ExistingStatusLine, result);
        Assert.Equal(original, File.ReadAllText(_settingsPath));
        Assert.False(Directory.Exists(_backupDir)); // No write → no backup churn.
    }

    [Fact]
    public void Install_OurOwnBridgeAlreadyRegistered_ReportsAlreadyInstalled()
    {
        File.WriteAllText(
            _settingsPath,
            """{"statusLine":{"type":"command","command":"\"C:\\x\\PulseBar.Bridge.exe\" claude-statusline"}}""");

        var result = ClaudeSettingsInstaller.Install(_settingsPath, BridgeCommand, _backupDir);

        Assert.Equal(StatuslineInstallResult.AlreadyInstalled, result);
    }

    [Fact]
    public void Install_CorruptSettings_AbortsWithoutModifying()
    {
        File.WriteAllText(_settingsPath, "{ broken json !!");

        var result = ClaudeSettingsInstaller.Install(_settingsPath, BridgeCommand, _backupDir);

        Assert.Equal(StatuslineInstallResult.SettingsUnreadable, result);
        Assert.Equal("{ broken json !!", File.ReadAllText(_settingsPath));
    }

    [Theory]
    [InlineData(@"C:\Users\chan\AppData\Local\PulseBar\bridge.exe", "/mnt/c/Users/chan/AppData/Local/PulseBar/bridge.exe")]
    [InlineData(@"D:\tools\b.exe", "/mnt/d/tools/b.exe")]
    public void ToWslPath_ConvertsDriveLetters(string windows, string expected)
    {
        Assert.Equal(expected, ClaudeSettingsInstaller.ToWslPath(windows));
    }

    [Fact]
    public void BuildBridgeCommand_WslVariant_UsesInteropPath()
    {
        var command = ClaudeSettingsInstaller.BuildBridgeCommand(
            @"C:\Apps\PulseBar.Bridge.exe", @"C:\Data\claude-status.json", forWsl: true);

        Assert.StartsWith("\"/mnt/c/Apps/PulseBar.Bridge.exe\"", command);
        Assert.Contains("--output 'C:\\Data\\claude-status.json'", command);
    }
}
