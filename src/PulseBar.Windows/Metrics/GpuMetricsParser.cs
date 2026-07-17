namespace PulseBar.Windows.Metrics;

/// <summary>
/// Pure parsing/aggregation for PDH GPU counters.
/// Engine instances look like: pid_1234_luid_0x00000000_0x0000C6C7_phys_0_engtype_3D
/// Memory instances look like: luid_0x00000000_0x0000C6C7_phys_0
/// </summary>
public static class GpuMetricsParser
{
    /// <summary>Extracts (luidKey, engineType) from a GPU Engine instance name.</summary>
    public static (string LuidKey, string EngineType)? ParseEngineInstance(string instance)
    {
        var luidKey = ExtractLuidKey(instance);
        if (luidKey is null)
        {
            return null;
        }

        const string engMarker = "engtype_";
        var engIndex = instance.LastIndexOf(engMarker, StringComparison.OrdinalIgnoreCase);
        if (engIndex < 0)
        {
            return null;
        }

        var engineType = instance[(engIndex + engMarker.Length)..];
        return engineType.Length == 0 ? null : (luidKey, engineType);
    }

    /// <summary>Extracts the luid key ("luid_0x..._0x...") from any GPU counter instance name.</summary>
    public static string? ExtractLuidKey(string instance)
    {
        const string marker = "luid_";
        var start = instance.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        // luid_0xAAAAAAAA_0xBBBBBBBB → marker + two "0x........" parts joined by '_'
        var rest = instance[start..];
        var parts = rest.Split('_');
        if (parts.Length < 3 || !parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || !parts[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{parts[0]}_{parts[1]}_{parts[2]}";
    }

    /// <summary>
    /// Task-Manager-like adapter utilization: per adapter, sum each engine type
    /// across processes, then take the busiest engine type. Clamped to 0–100.
    /// Never naively sums all engines (that produces 300% style numbers).
    /// </summary>
    public static IReadOnlyDictionary<string, double> AggregateUtilization(
        IEnumerable<(string Instance, double Value)> engineValues)
    {
        var perAdapterEngine = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (instance, value) in engineValues)
        {
            if (value <= 0 || ParseEngineInstance(instance) is not var (luidKey, engineType))
            {
                continue;
            }

            if (!perAdapterEngine.TryGetValue(luidKey, out var engines))
            {
                engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                perAdapterEngine[luidKey] = engines;
            }

            engines[engineType] = engines.GetValueOrDefault(engineType) + value;
        }

        return perAdapterEngine.ToDictionary(
            kv => kv.Key,
            kv => Math.Clamp(kv.Value.Values.Max(), 0.0, 100.0),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Sums dedicated memory usage per adapter from GPU Adapter Memory instances.</summary>
    public static IReadOnlyDictionary<string, double> AggregateDedicatedMemory(
        IEnumerable<(string Instance, double Value)> memoryValues)
    {
        var perAdapter = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, value) in memoryValues)
        {
            if (value < 0 || ExtractLuidKey(instance) is not { } luidKey)
            {
                continue;
            }

            perAdapter[luidKey] = perAdapter.GetValueOrDefault(luidKey) + value;
        }

        return perAdapter;
    }
}
