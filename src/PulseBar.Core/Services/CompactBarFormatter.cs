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

    private static string Token(string label, string value, bool compact)
        => compact ? label + value : label + " " + value;
}
