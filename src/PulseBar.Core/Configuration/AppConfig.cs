using System.Text.Json.Serialization;
using PulseBar.Core.Models;

namespace PulseBar.Core.Configuration;

public enum DisplayMode
{
    TaskbarOverlay,
    Floating
}

public enum BarLayout
{
    OneLine,
    TwoLine,
    UltraCompact
}

public enum NetworkMode
{
    ActiveAdapters,
    SelectedAdapters
}

public sealed class AppearanceConfig
{
    public string Language { get; set; } = "ko-KR";
    public DisplayMode Mode { get; set; } = DisplayMode.TaskbarOverlay;
    public BarLayout Layout { get; set; } = BarLayout.TwoLine;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 11;
    public double Opacity { get; set; } = 0.96;
    public string TargetMonitor { get; set; } = "primary";

    /// <summary>Saved floating-mode position per monitor device name (DIP coordinates).</summary>
    public Dictionary<string, FloatingPosition> FloatingPositions { get; set; } = [];
}

public sealed class FloatingPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class MetricsConfig
{
    public int PollIntervalMs { get; set; } = 1000;
    public bool ShowCpu { get; set; } = true;
    public bool ShowMemory { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowVram { get; set; } = true;
    public bool ShowDiskActivity { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public NetworkMode NetworkMode { get; set; } = NetworkMode.ActiveAdapters;
    public List<string> IncludedAdapters { get; set; } = [];
    public List<string> ExcludedAdapters { get; set; } = [];
}

public sealed class ProviderProfileConfig
{
    public string Id { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public ExecutionEnvironmentType Environment { get; set; } = ExecutionEnvironmentType.WindowsNative;
    public string? ExecutablePath { get; set; }
    public string? WslDistribution { get; set; }
    public string? LinuxHome { get; set; }
    public bool Enabled { get; set; }
    public int RefreshIntervalSeconds { get; set; } = 60;

    public ProviderProfile ToProfile()
        => new(Id, ProviderId, Environment, ExecutablePath, WslDistribution, LinuxHome, Enabled);
}

public sealed class ThresholdsConfig
{
    public double WarningPercent { get; set; } = 70;
    public double HighPercent { get; set; } = 85;
    public double CriticalPercent { get; set; } = 95;
}

public sealed class ClaudeConfig
{
    /// <summary>Model-id matcher for the local Fable token aggregation; '*' suffix = prefix match.</summary>
    public string FableModelMatcher { get; set; } = "claude-fable-5";
}

public sealed class StorageConfig
{
    public int TokenEventRetentionDays { get; set; } = 30;
    public int LogRetentionDays { get; set; } = 7;
    public bool StoreSystemHistory { get; set; }
}

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public AppearanceConfig Appearance { get; set; } = new();
    public MetricsConfig Metrics { get; set; } = new();
    public List<ProviderProfileConfig> Providers { get; set; } = [];
    public ThresholdsConfig Thresholds { get; set; } = new();
    public ClaudeConfig Claude { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();

    [JsonIgnore]
    public static readonly string[] SupportedLanguages = ["ko-KR", "en-US"];
}
