using System.Text.Json.Nodes;
using PulseBar.Providers.Claude.OpenTelemetry;

namespace PulseBar.Claude.Tests;

public sealed class OtelEnvInstallerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _settingsPath;
    private readonly string _backupDir;

    public OtelEnvInstallerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PulseBarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settingsPath = Path.Combine(_dir, "settings.json");
        _backupDir = Path.Combine(_dir, "backups");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Endpoint = "http://127.0.0.1:4318";
    private const string Secret = "s3cret";

    [Fact]
    public void Install_AddsEnvKeys_PreservingExistingSettings()
    {
        File.WriteAllText(_settingsPath, """{"model":"opusplan","env":{"MY_VAR":"x"}}""");

        var result = OtelEnvInstaller.Install(_settingsPath, Endpoint, Secret, _backupDir);

        Assert.Equal(OtelInstallResult.Installed, result);
        var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!;
        Assert.Equal("x", root["env"]!["MY_VAR"]!.GetValue<string>());
        Assert.Equal("1", root["env"]!["CLAUDE_CODE_ENABLE_TELEMETRY"]!.GetValue<string>());
        Assert.Equal("http/json", root["env"]!["OTEL_EXPORTER_OTLP_PROTOCOL"]!.GetValue<string>());
        Assert.Equal($"Authorization=Bearer {Secret}", root["env"]!["OTEL_EXPORTER_OTLP_HEADERS"]!.GetValue<string>());
        Assert.Equal("opusplan", root["model"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(_backupDir));
    }

    [Fact]
    public void Install_SameValuesAlreadyPresent_ReportsAlreadyInstalled()
    {
        OtelEnvInstaller.Install(_settingsPath, Endpoint, Secret, _backupDir);

        var second = OtelEnvInstaller.Install(_settingsPath, Endpoint, Secret, _backupDir);

        Assert.Equal(OtelInstallResult.AlreadyInstalled, second);
    }

    [Fact]
    public void Install_ForeignOtelEndpoint_IsNeverOverwritten()
    {
        File.WriteAllText(
            _settingsPath,
            """{"env":{"OTEL_EXPORTER_OTLP_ENDPOINT":"http://my-collector:4317"}}""");
        var original = File.ReadAllText(_settingsPath);

        var result = OtelEnvInstaller.Install(_settingsPath, Endpoint, Secret, _backupDir);

        Assert.Equal(OtelInstallResult.ConflictingEnv, result);
        Assert.Equal(original, File.ReadAllText(_settingsPath));
    }

    [Fact]
    public void Install_CorruptSettings_Aborts()
    {
        File.WriteAllText(_settingsPath, "not json");

        Assert.Equal(
            OtelInstallResult.SettingsUnreadable,
            OtelEnvInstaller.Install(_settingsPath, Endpoint, Secret, _backupDir));
    }

    [Fact]
    public void BuildEnv_NeverEnablesPromptLogging()
    {
        var env = OtelEnvInstaller.BuildEnv(Endpoint, Secret);

        Assert.DoesNotContain(env.Keys, k => k.Contains("PROMPT", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(env.Keys, k => k.Contains("USER_CONTENT", StringComparison.OrdinalIgnoreCase));
    }
}
