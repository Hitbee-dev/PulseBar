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
    /// Wraps an existing foreign statusLine so both PulseBar and the original HUD
    /// receive the statusline JSON. Requires explicit user consent (never called
    /// automatically). The original command is preserved verbatim in a sidecar
    /// script next to settings.json — this avoids any quote-escaping of the
    /// original command inside the wrapper string.
    /// </summary>
    public static StatuslineInstallResult InstallWrapped(
        string settingsPath,
        string bridgeExePath,
        string outputPath,
        bool forWsl,
        string backupDir)
    {
        if (!File.Exists(settingsPath))
        {
            return Install(settingsPath, BuildBridgeCommand(bridgeExePath, outputPath, forWsl), backupDir);
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject
                ?? throw new JsonException("settings root is not an object");
        }
        catch (JsonException)
        {
            return StatuslineInstallResult.SettingsUnreadable;
        }

        if (root.TryGetPropertyValue("statusLine", out var existing) && existing is not null)
        {
            var existingCommand = existing["command"]?.GetValue<string>() ?? "";
            if (existingCommand.Contains(BridgeMarker, StringComparison.OrdinalIgnoreCase))
            {
                return StatuslineInstallResult.AlreadyInstalled;
            }

            Backup(settingsPath, backupDir);

            var settingsDir = Path.GetDirectoryName(settingsPath)!;
            string wrapperCommand;
            if (forWsl)
            {
                var scriptPath = Path.Combine(settingsDir, "pulsebar-statusline-original.sh");
                File.WriteAllText(scriptPath, existingCommand + "\n");
                wrapperCommand = BuildWslWrapperCommand(bridgeExePath, outputPath);
            }
            else
            {
                var scriptPath = Path.Combine(settingsDir, "pulsebar-statusline-original.cmd");
                File.WriteAllText(scriptPath, "@echo off\r\n" + existingCommand + "\r\n");
                wrapperCommand =
                    $"\"{bridgeExePath}\" claude-statusline --output \"{outputPath}\"" +
                    $" --passthrough \"\\\"{scriptPath}\\\"\"";
            }

            var padding = existing["padding"];
            var statusLine = new JsonObject
            {
                ["type"] = "command",
                ["command"] = wrapperCommand,
            };
            if (padding is not null)
            {
                statusLine["padding"] = padding.DeepClone();
            }

            root["statusLine"] = statusLine;
            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var tmp = settingsPath + ".pulsebar.tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, settingsPath, overwrite: true);
            return StatuslineInstallResult.Installed;
        }

        return Install(settingsPath, BuildBridgeCommand(bridgeExePath, outputPath, forWsl), backupDir);
    }

    /// <summary>
    /// WSL wrapper: capture stdin once, feed it to the (Windows) bridge via interop,
    /// then to the preserved original command; the original's stdout is the statusline.
    /// </summary>
    private static string BuildWslWrapperCommand(string bridgeExePath, string outputPath)
    {
        var wslExe = ToWslPath(bridgeExePath);
        return "input=$(cat); " +
               $"printf %s \"$input\" | '{wslExe}' claude-statusline --output '{outputPath}' >/dev/null 2>&1; " +
               "printf %s \"$input\" | sh \"$HOME/.claude/pulsebar-statusline-original.sh\"";
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
