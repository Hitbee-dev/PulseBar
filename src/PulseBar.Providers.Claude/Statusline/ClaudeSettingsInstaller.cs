using System.Text.Json;
using System.Text.Json.Nodes;

namespace PulseBar.Providers.Claude.Statusline;

public enum StatuslineInstallResult
{
    Installed,
    AlreadyInstalled,

    /// <summary>A foreign statusLine exists — never overwritten; manual merge required.</summary>
    ExistingStatusLine,

    /// <summary>settings.json could not be parsed — nothing was modified.</summary>
    SettingsUnreadable,
}

/// <summary>
/// Registers the PulseBar bridge as Claude Code's statusLine command.
/// Rules (spec §10.2/§12.3): timestamped backup before any write, JSON parse
/// failure aborts, an existing foreign statusLine is never overwritten,
/// writes are atomic, and unknown settings fields are preserved verbatim.
/// </summary>
public static class ClaudeSettingsInstaller
{
    private const string BridgeMarker = "PulseBar.Bridge";

    public static StatuslineInstallResult Install(
        string settingsPath,
        string bridgeCommand,
        string backupDir)
    {
        JsonObject root;

        if (File.Exists(settingsPath))
        {
            string text;
            try
            {
                text = File.ReadAllText(settingsPath);
            }
            catch (IOException)
            {
                return StatuslineInstallResult.SettingsUnreadable;
            }

            try
            {
                root = JsonNode.Parse(text) as JsonObject
                    ?? throw new JsonException("settings root is not an object");
            }
            catch (JsonException)
            {
                return StatuslineInstallResult.SettingsUnreadable;
            }

            if (root.TryGetPropertyValue("statusLine", out var existing) && existing is not null)
            {
                var existingCommand = existing["command"]?.GetValue<string>() ?? "";
                return existingCommand.Contains(BridgeMarker, StringComparison.OrdinalIgnoreCase)
                    ? StatuslineInstallResult.AlreadyInstalled
                    : StatuslineInstallResult.ExistingStatusLine;
            }

            Backup(settingsPath, backupDir);
        }
        else
        {
            root = [];
        }

        root["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = bridgeCommand,
            ["padding"] = 0,
        };

        WriteAtomically(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return StatuslineInstallResult.Installed;
    }

    /// <summary>
    /// Builds the statusLine command string. For WSL profiles the Windows bridge
    /// exe is invoked through interop (/mnt/c/...), while --output stays a Windows
    /// path because the bridge itself is a Windows process.
    /// </summary>
    public static string BuildBridgeCommand(string bridgeExePath, string outputPath, bool forWsl)
    {
        if (!forWsl)
        {
            return $"\"{bridgeExePath}\" claude-statusline --output \"{outputPath}\"";
        }

        var wslExePath = ToWslPath(bridgeExePath);
        return $"\"{wslExePath}\" claude-statusline --output '{outputPath}'";
    }

    /// <summary>C:\Users\chan\x → /mnt/c/Users/chan/x</summary>
    public static string ToWslPath(string windowsPath)
    {
        if (windowsPath.Length < 3 || windowsPath[1] != ':')
        {
            return windowsPath;
        }

        var drive = char.ToLowerInvariant(windowsPath[0]);
        return $"/mnt/{drive}{windowsPath[2..].Replace('\\', '/')}";
    }

    private static void Backup(string settingsPath, string backupDir)
    {
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(
            backupDir,
            $"claude-settings.{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.Copy(settingsPath, backupPath, overwrite: true);
    }

    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".pulsebar.tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
