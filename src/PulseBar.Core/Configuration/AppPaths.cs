namespace PulseBar.Core.Configuration;

public interface IAppPaths
{
    string Root { get; }
    string ConfigFile { get; }
    string DataDir { get; }
    string DatabaseFile { get; }
    string BridgeDir { get; }
    string ClaudeStatusCacheFile { get; }
    string LogsDir { get; }
    string BackupsDir { get; }
}

/// <summary>
/// All PulseBar files live under %LOCALAPPDATA%\PulseBar (overridable for tests).
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public AppPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PulseBar"))
    {
    }

    public AppPaths(string root)
    {
        Root = root;
        ConfigFile = Path.Combine(root, "config.json");
        DataDir = Path.Combine(root, "data");
        DatabaseFile = Path.Combine(DataDir, "pulsebar.db");
        BridgeDir = Path.Combine(root, "bridge");
        ClaudeStatusCacheFile = Path.Combine(BridgeDir, "claude-status.json");
        LogsDir = Path.Combine(root, "logs");
        BackupsDir = Path.Combine(root, "backups");
    }

    public string Root { get; }
    public string ConfigFile { get; }
    public string DataDir { get; }
    public string DatabaseFile { get; }
    public string BridgeDir { get; }
    public string ClaudeStatusCacheFile { get; }
    public string LogsDir { get; }
    public string BackupsDir { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BridgeDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(BackupsDir);
    }
}
