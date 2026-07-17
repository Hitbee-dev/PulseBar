namespace PulseBar.Windows.Metrics;

/// <summary>Pure filtering/summing of PDH Network Interface instances.</summary>
public static class NetworkAdapterFilter
{
    private static readonly string[] AlwaysExcluded =
    [
        "loopback",
        "isatap",
        "teredo",
    ];

    /// <summary>
    /// Sums adapter values. Loopback/tunnel pseudo-adapters are always excluded.
    /// When <paramref name="includedAdapters"/> is non-empty, only matching adapters count;
    /// otherwise all adapters minus <paramref name="excludedAdapters"/> count.
    /// Matching is case-insensitive substring.
    /// </summary>
    public static double Sum(
        IEnumerable<(string Instance, double Value)> values,
        IReadOnlyList<string> includedAdapters,
        IReadOnlyList<string> excludedAdapters)
    {
        double total = 0;
        foreach (var (instance, value) in values)
        {
            if (value < 0 || Matches(instance, AlwaysExcluded))
            {
                continue;
            }

            if (includedAdapters.Count > 0)
            {
                if (Matches(instance, includedAdapters))
                {
                    total += value;
                }

                continue;
            }

            if (!Matches(instance, excludedAdapters))
            {
                total += value;
            }
        }

        return total;
    }

    private static bool Matches(string instance, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern)
                && instance.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(string instance, string[] patterns)
        => Matches(instance, (IReadOnlyList<string>)patterns);
}
