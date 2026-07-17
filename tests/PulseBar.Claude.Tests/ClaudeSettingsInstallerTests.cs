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

    private const string ForeignHudCommand =
        "bash -c 'cols=$(stty size </dev/tty | awk '\"'\"'{print $2}'\"'\"'); exec node hud.js'";

    [Fact]
    public void InstallWrapped_ForeignStatusline_WrapsAndPreservesOriginalVerbatim()
    {
        File.WriteAllText(
            _settingsPath,
            System.Text.Json.Nodes.JsonNode.Parse(
                $$"""{"statusLine":{"type":"command","command":{{System.Text.Json.JsonSerializer.Serialize(ForeignHudCommand)}},"padding":1},"model":"x"}""")!
                .ToJsonString());

        var result = ClaudeSettingsInstaller.InstallWrapped(
            _settingsPath, @"C:\Apps\PulseBar.Bridge.exe", @"C:\Data\claude-status.json",
            forWsl: true, _backupDir);

        Assert.Equal(StatuslineInstallResult.Installed, result);

        // Original command preserved verbatim in the sidecar (nasty quoting untouched).
        var sidecar = Path.Combine(_dir, "pulsebar-statusline-original.sh");
        Assert.Equal(ForeignHudCommand + "\n", File.ReadAllText(sidecar));

        var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!;
        var command = root["statusLine"]!["command"]!.GetValue<string>();
        Assert.Contains("/mnt/c/Apps/PulseBar.Bridge.exe", command);
        Assert.Contains("pulsebar-statusline-original.sh", command);
        Assert.Contains("input=$(cat)", command);
        Assert.Equal(1, root["statusLine"]!["padding"]!.GetValue<int>());
        Assert.Equal("x", root["model"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(_backupDir));
    }

    [Fact]
    public void InstallWrapped_BridgeAlreadyPresent_ReportsAlreadyInstalled()
    {
        File.WriteAllText(
            _settingsPath,
            """{"statusLine":{"type":"command","command":"x | PulseBar.Bridge.exe claude-statusline"}}""");

        var result = ClaudeSettingsInstaller.InstallWrapped(
            _settingsPath, @"C:\Apps\PulseBar.Bridge.exe", @"C:\Data\out.json", forWsl: true, _backupDir);

        Assert.Equal(StatuslineInstallResult.AlreadyInstalled, result);
    }

    [Fact]
    public void InstallWrapped_NoStatusline_FallsBackToPlainInstall()
    {
        File.WriteAllText(_settingsPath, """{"model":"x"}""");

        var result = ClaudeSettingsInstaller.InstallWrapped(
            _settingsPath, @"C:\Apps\PulseBar.Bridge.exe", @"C:\Data\out.json", forWsl: false, _backupDir);

        Assert.Equal(StatuslineInstallResult.Installed, result);
        var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!;
        Assert.DoesNotContain("passthrough", root["statusLine"]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void InstallWrapped_WindowsForeignStatusline_UsesCmdSidecarWithPassthrough()
    {
        File.WriteAllText(
            _settingsPath,
            """{"statusLine":{"type":"command","command":"node C:/Users/x/.claude/hud/omc-hud.mjs"}}""");

        var result = ClaudeSettingsInstaller.InstallWrapped(
            _settingsPath, @"C:\Apps\PulseBar.Bridge.exe", @"C:\Data\out.json", forWsl: false, _backupDir);

        Assert.Equal(StatuslineInstallResult.Installed, result);
        var sidecar = Path.Combine(_dir, "pulsebar-statusline-original.cmd");
        Assert.Contains("node C:/Users/x/.claude/hud/omc-hud.mjs", File.ReadAllText(sidecar));
        var command = JsonNode.Parse(File.ReadAllText(_settingsPath))!["statusLine"]!["command"]!.GetValue<string>();
        Assert.Contains("--passthrough", command);
        Assert.Contains("pulsebar-statusline-original.cmd", command);
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
