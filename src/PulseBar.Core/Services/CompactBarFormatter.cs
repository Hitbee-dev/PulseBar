using PulseBar.Core.Configuration;
using PulseBar.Core.Models;

namespace PulseBar.Core.Services;

/// <summary>Visual role of one compact-bar text run (colored/weighted by the UI).</summary>
public enum BarSegmentKind
{
    /// <summary>Metric label (CPU/RAM/…): muted.</summary>
    Label,

    /// <summary>Normal value: bold, high contrast.</summary>
    Value,

    /// <summary>Value ≥ warning threshold (default 70%).</summary>
    ValueWarning,

    /// <summary>Value ≥ high threshold (default 85%).</summary>
    ValueHigh,

    /// <summary>Value ≥ critical threshold (default 95%).</summary>
    ValueCritical,

    /// <summary>Whitespace / "|" / "·" between groups: dimmed.</summary>
    Separator,

    /// <summary>"Claude" provider name: brand tint.</summary>
    ProviderClaude,

    /// <summary>"Codex" provider name: brand tint.</summary>
    ProviderCodex,

    /// <summary>Network download token (↓…).</summary>
    Down,

    /// <summary>Network upload token (↑…).</summary>
    Up,

    /// <summary>Stale/auth/error status text: dimmed gray.</summary>
    Stale,
}

public sealed record BarSegment(string Text, BarSegmentKind Kind);

/// <summary>
/// Builds the taskbar compact-bar content as colored segments. Tokens
/// (CPU/RAM/GPU/VRAM/D/↓↑) are fixed across languages per the UI spec; only
/// availability differs ("—" when unknown). Plain-string variants concatenate
/// the same segments (used for logs/tests).
/// </summary>
public static class CompactBarFormatter
{
    public static string SystemLine(SystemMetrics m, MetricsConfig config, BarLayout layout)
        => string.Concat(SystemSegments(m, config, layout, new ThresholdsConfig()).Select(s => s.Text));

    public static IReadOnlyList<BarSegment> SystemSegments(
        SystemMetrics m,
        MetricsConfig config,
        BarLayout layout,
        ThresholdsConfig thresholds)
    {
        var compact = layout == BarLayout.UltraCompact;
        var gap = compact ? " " : "  ";
        var segments = new List<BarSegment>(16);

        void Metric(string label, string compactLabel, string value, double? percentForColor)
        {
            if (segments.Count > 0)
            {
                segments.Add(new BarSegment(gap, BarSegmentKind.Separator));
            }

            segments.Add(new BarSegment(compact ? compactLabel : label + " ", BarSegmentKind.Label));
            segments.Add(new BarSegment(value, ClassifyPercent(percentForColor, thresholds)));
        }

        if (config.ShowCpu)
        {
            Metric("CPU", "C", UnitFormatter.Percent(m.CpuPercent), m.CpuPercent);
        }

        if (config.ShowMemory)
        {
            Metric("RAM", "M", UnitFormatter.Percent(m.MemoryUsedPercent), m.MemoryUsedPercent);
        }

        if (config.ShowGpu)
        {
            Metric("GPU", "G", UnitFormatter.Percent(m.GpuPercent), m.GpuPercent);
        }

        if (config.ShowVram)
        {
            var value = m.VramUsedBytes is { } used ? UnitFormatter.BytesCompact(used) : "—";
            double? vramPercent = m.VramUsedBytes is { } u && m.VramTotalBytes is { } total and > 0
                ? 100.0 * u / total
                : null;
            Metric("VRAM", "V", value, vramPercent);
        }

        if (config.ShowDiskActivity)
        {
            Metric("D", "D", UnitFormatter.Percent(m.DiskActivePercent), m.DiskActivePercent);
        }

        if (config.ShowNetwork)
        {
            if (segments.Count > 0)
            {
                segments.Add(new BarSegment(gap, BarSegmentKind.Separator));
            }

            var down = m.NetworkReceivedBytesPerSec is { } rx ? UnitFormatter.BytesCompact(rx) : "—";
            var up = m.NetworkSentBytesPerSec is { } tx ? UnitFormatter.BytesCompact(tx) : "—";
            segments.Add(new BarSegment($"↓{down}", BarSegmentKind.Down));
            segments.Add(new BarSegment(" ", BarSegmentKind.Separator));
            segments.Add(new BarSegment($"↑{up}", BarSegmentKind.Up));
        }

        return segments;
    }

    /// <summary>Spec §5.4 color steps: normal → warning → high → critical.</summary>
    public static BarSegmentKind ClassifyPercent(double? percent, ThresholdsConfig thresholds)
        => percent switch
        {
            null => BarSegmentKind.Value,
            var p when p >= thresholds.CriticalPercent => BarSegmentKind.ValueCritical,
            var p when p >= thresholds.HighPercent => BarSegmentKind.ValueHigh,
            var p when p >= thresholds.WarningPercent => BarSegmentKind.ValueWarning,
            _ => BarSegmentKind.Value,
        };

    /// <summary>
    /// "Claude 5h 41 · W 68 | Codex 5h 25 · W 18" — buckets are matched by duration
    /// (300/10080 min), never by name. Providers with no usable data show their state.
    /// </summary>
    public static string ProviderLine(
        IReadOnlyList<UsageSnapshot> snapshots,
        Func<string, string> localize)
        => string.Concat(ProviderSegments(snapshots, localize, new ThresholdsConfig()).Select(s => s.Text));

    public static IReadOnlyList<BarSegment> ProviderSegments(
        IReadOnlyList<UsageSnapshot> snapshots,
        Func<string, string> localize,
        ThresholdsConfig thresholds)
    {
        if (snapshots.Count == 0)
        {
            return [new BarSegment(localize("Common_NotConnected"), BarSegmentKind.Stale)];
        }

        var segments = new List<BarSegment>(16);
        foreach (var snapshot in snapshots)
        {
            if (segments.Count > 0)
            {
                segments.Add(new BarSegment("  |  ", BarSegmentKind.Separator));
            }

            AppendProvider(segments, snapshot, localize, thresholds);
        }

        return segments;
    }

    private static void AppendProvider(
        List<BarSegment> segments,
        UsageSnapshot snapshot,
        Func<string, string> localize,
        ThresholdsConfig thresholds)
    {
        var (name, nameKind) = snapshot.ProviderId switch
        {
            "codex" => ("Codex", BarSegmentKind.ProviderCodex),
            "claude" => ("Claude", BarSegmentKind.ProviderClaude),
            _ => (snapshot.ProviderId, BarSegmentKind.Label),
        };
        segments.Add(new BarSegment(name, nameKind));

        switch (snapshot.Freshness)
        {
            case DataFreshness.AuthenticationRequired:
                segments.Add(new BarSegment($" {localize("Freshness_AuthenticationRequired")}", BarSegmentKind.Stale));
                return;
            case DataFreshness.Error:
                segments.Add(new BarSegment($" {localize("Freshness_Error")}", BarSegmentKind.ValueHigh));
                return;
            case DataFreshness.Unavailable:
                segments.Add(new BarSegment(" —", BarSegmentKind.Stale));
                return;
        }

        var five = snapshot.Windows.FirstOrDefault(w => w.Duration == TimeSpan.FromMinutes(300));
        var week = snapshot.Windows.FirstOrDefault(w => w.Duration == TimeSpan.FromMinutes(10080));

        var wroteWindow = false;
        void Window(string label, UsageWindow? window)
        {
            if (window is null)
            {
                return;
            }

            segments.Add(new BarSegment(wroteWindow ? $" · {label} " : $" {label} ", BarSegmentKind.Label));
            segments.Add(new BarSegment(
                UnitFormatter.Percent(window.UsedPercent),
                ClassifyPercent(window.UsedPercent, thresholds)));
            wroteWindow = true;
        }

        Window("5h", five);
        Window("W", week);

        if (!wroteWindow)
        {
            var first = snapshot.Windows.FirstOrDefault();
            if (first is null)
            {
                segments.Add(new BarSegment(" —", BarSegmentKind.Stale));
            }
            else
            {
                segments.Add(new BarSegment($" {first.DisplayName} ", BarSegmentKind.Label));
                segments.Add(new BarSegment(
                    UnitFormatter.Percent(first.UsedPercent),
                    ClassifyPercent(first.UsedPercent, thresholds)));
            }
        }

        if (snapshot.Freshness == DataFreshness.Stale)
        {
            segments.Add(new BarSegment($" ({localize("Freshness_Stale")})", BarSegmentKind.Stale));
        }
    }
}
