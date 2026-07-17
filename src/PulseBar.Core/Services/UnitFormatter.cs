using System.Globalization;

namespace PulseBar.Core.Services;

/// <summary>Compact numeric formatting for the taskbar bar (invariant culture).</summary>
public static class UnitFormatter
{
    /// <summary>1234567 → "1.2M"; 950 → "950"; values &lt; 10 keep one decimal.</summary>
    public static string BytesCompact(double bytes)
    {
        if (bytes < 0 || double.IsNaN(bytes))
        {
            return "0";
        }

        var (value, suffix) = Scale(bytes);
        return FormatScaled(value) + suffix;
    }

    /// <summary>Bytes per second with explicit unit: "32.1 MB/s".</summary>
    public static string BytesPerSecond(double bytesPerSec)
    {
        if (bytesPerSec < 0 || double.IsNaN(bytesPerSec))
        {
            bytesPerSec = 0;
        }

        var (value, suffix) = Scale(bytesPerSec);
        var unit = suffix.Length == 0 ? "B/s" : suffix + "B/s";
        return FormatScaled(value) + " " + unit;
    }

    /// <summary>Percent without the sign: 14.3 → "14"; null → "—".</summary>
    public static string Percent(double? percent)
        => percent is null
            ? "—"
            : Math.Round(percent.Value).ToString(CultureInfo.InvariantCulture);

    private static (double Value, string Suffix) Scale(double value)
        => value switch
        {
            >= 1024L * 1024 * 1024 * 1024 => (value / (1024L * 1024 * 1024 * 1024), "T"),
            >= 1024 * 1024 * 1024 => (value / (1024.0 * 1024 * 1024), "G"),
            >= 1024 * 1024 => (value / (1024.0 * 1024), "M"),
            >= 1024 => (value / 1024.0, "K"),
            _ => (value, ""),
        };

    private static string FormatScaled(double value)
        => value < 10 && value != Math.Floor(value)
            ? value.ToString("0.#", CultureInfo.InvariantCulture)
            : Math.Round(value).ToString(CultureInfo.InvariantCulture);
}
