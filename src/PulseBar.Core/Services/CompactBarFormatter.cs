using PulseBar.Core.Configuration;
using PulseBar.Core.Models;

namespace PulseBar.Core.Services;

/// <summary>
/// Builds the taskbar compact-bar text. Tokens (CPU/RAM/GPU/VRAM/D/↓↑) are fixed
/// across languages per the UI spec; only availability differs ("—" when unknown).
/// </summary>
public static class CompactBarFormatter
{
    public static string SystemLine(SystemMetrics m, MetricsConfig config, BarLayout layout)
    {
        var compact = layout == BarLayout.UltraCompact;
        var parts = new List<string>(7);

        if (config.ShowCpu)
        {
            parts.Add(Token(compact ? "C" : "CPU", UnitFormatter.Percent(m.CpuPercent), compact));
        }

        if (config.ShowMemory)
        {
            parts.Add(Token(compact ? "M" : "RAM", UnitFormatter.Percent(m.MemoryUsedPercent), compact));
        }

        if (config.ShowGpu)
        {
            parts.Add(Token(compact ? "G" : "GPU", UnitFormatter.Percent(m.GpuPercent), compact));
        }

        if (config.ShowVram)
        {
            var value = m.VramUsedBytes is { } used ? UnitFormatter.BytesCompact(used) : "—";
            parts.Add(Token(compact ? "V" : "VRAM", value, compact));
        }

        if (config.ShowDiskActivity)
        {
            parts.Add(Token("D", UnitFormatter.Percent(m.DiskActivePercent), compact));
        }

        if (config.ShowNetwork)
        {
            var down = m.NetworkReceivedBytesPerSec is { } rx ? UnitFormatter.BytesCompact(rx) : "—";
            var up = m.NetworkSentBytesPerSec is { } tx ? UnitFormatter.BytesCompact(tx) : "—";
            parts.Add($"↓{down} ↑{up}");
        }

        return string.Join(compact ? " " : "  ", parts);
    }

    /// <summary>
    /// "Claude 5h 41 · W 68 | Codex 5h 25 · W 18" — buckets are matched by duration
    /// (300/10080 min), never by name. Providers with no usable data show their state.
    /// </summary>
    public static string ProviderLine(
        IReadOnlyList<UsageSnapshot> snapshots,
        Func<string, string> localize)
    {
        if (snapshots.Count == 0)
        {
            return localize("Common_NotConnected");
        }

        var parts = new List<string>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            parts.Add(FormatProvider(snapshot, localize));
        }

        return string.Join("  |  ", parts);
    }

    private static string FormatProvider(UsageSnapshot snapshot, Func<string, string> localize)
    {
        var name = snapshot.ProviderId switch
        {
            "codex" => "Codex",
            "claude" => "Claude",
            _ => snapshot.ProviderId,
        };

        switch (snapshot.Freshness)
        {
            case DataFreshness.AuthenticationRequired:
                return $"{name} {localize("Freshness_AuthenticationRequired")}";
            case DataFreshness.Error:
                return $"{name} {localize("Freshness_Error")}";
            case DataFreshness.Unavailable:
                return $"{name} —";
        }

        var five = snapshot.Windows.FirstOrDefault(w => w.Duration == TimeSpan.FromMinutes(300));
        var week = snapshot.Windows.FirstOrDefault(w => w.Duration == TimeSpan.FromMinutes(10080));

        var segments = new List<string>(2);
        if (five is not null)
        {
            segments.Add($"5h {UnitFormatter.Percent(five.UsedPercent)}");
        }

        if (week is not null)
        {
            segments.Add($"W {UnitFormatter.Percent(week.UsedPercent)}");
        }

        if (segments.Count == 0)
        {
            var first = snapshot.Windows.FirstOrDefault();
            segments.Add(first is null ? "—" : $"{first.DisplayName} {UnitFormatter.Percent(first.UsedPercent)}");
        }

        var text = $"{name} {string.Join(" · ", segments)}";
        if (snapshot.Freshness == DataFreshness.Stale)
        {
            text += $" ({localize("Freshness_Stale")})";
        }

        return text;
    }

    private static string Token(string label, string value, bool compact)
        => compact ? label + value : label + " " + value;
}
