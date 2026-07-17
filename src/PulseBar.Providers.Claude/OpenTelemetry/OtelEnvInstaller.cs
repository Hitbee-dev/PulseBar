using System.Text.Json;
using System.Text.Json.Nodes;

namespace PulseBar.Providers.Claude.OpenTelemetry;

public enum OtelInstallResult
{
    Installed,
    AlreadyInstalled,

    /// <summary>Foreign OTEL_* env values exist — nothing was modified (spec rule 11).</summary>
    ConflictingEnv,
    SettingsUnreadable,
}

/// <summary>
/// Adds the official Claude Code telemetry env vars pointing at the PulseBar
/// loopback receiver. Opt-in only — callers must have explicit user consent.
/// Backs up first, adds keys only when absent, never overwrites foreign values,
/// and never enables prompt/response logging options.
/// </summary>
public static class OtelEnvInstaller
{
    public static OtelInstallResult Install(
        string settingsPath,
        string endpoint,
        string secret,
        string backupDir)
    {
        var desired = BuildEnv(endpoint, secret);

        JsonObject root;
        if (File.Exists(settingsPath))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject
                    ?? throw new JsonException("settings root is not an object");
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return OtelInstallResult.SettingsUnreadable;
            }
        }
        else
        {
            root = [];
        }

        if (root["env"] is not JsonObject env)
        {
            env = [];
            root["env"] = env;
        }

        var allMatch = true;
        var anyConflict = false;
        foreach (var (key, value) in desired)
        {
            var existing = env[key]?.GetValue<string>();
            if (existing is null)
            {
                allMatch = false;
            }
            else if (!string.Equals(existing, value, StringComparison.Ordinal))
            {
                anyConflict = true;
            }
        }

        if (anyConflict)
        {
            return OtelInstallResult.ConflictingEnv;
        }

        if (allMatch)
        {
            return OtelInstallResult.AlreadyInstalled;
        }

        Backup(settingsPath, backupDir);

        foreach (var (key, value) in desired)
        {
            env[key] ??= value;
        }

        var tmp = settingsPath + ".pulsebar.tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, settingsPath, overwrite: true);
        return OtelInstallResult.Installed;
    }

    public static IReadOnlyDictionary<string, string> BuildEnv(string endpoint, string secret)
        => new Dictionary<string, string>
        {
            ["CLAUDE_CODE_ENABLE_TELEMETRY"] = "1",
            ["OTEL_LOGS_EXPORTER"] = "otlp",
            // PulseBar's receiver speaks the OTLP JSON encoding (no protobuf dependency).
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/json",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
            ["OTEL_EXPORTER_OTLP_HEADERS"] = $"Authorization=Bearer {secret}",
        };

    private static void Backup(string settingsPath, string backupDir)
    {
        if (!File.Exists(settingsPath))
        {
            return;
        }

        Directory.CreateDirectory(backupDir);
        File.Copy(
            settingsPath,
            Path.Combine(backupDir, $"claude-settings.{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"),
            overwrite: true);
    }
}
