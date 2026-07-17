using PulseBar.Windows.Interop;
using PulseBar.Windows.Metrics;

namespace PulseBar.Windows.Tests.Metrics;

/// <summary>
/// These run against the real Windows PDH/DXGI/kernel APIs (the test host is Windows).
/// They assert plausibility, not exact values.
/// </summary>
public class RealCountersIntegrationTests
{
    [Fact]
    public void MemorySampler_ReturnsPlausibleTotals()
    {
        var sample = MemorySampler.Sample();

        Assert.NotNull(sample);
        Assert.True(sample!.TotalBytes > 1024UL * 1024 * 1024, "expected at least 1 GB RAM");
        Assert.True(sample.AvailableBytes <= sample.TotalBytes);
        Assert.InRange(sample.UsedPercent, 0.0, 100.0);
    }

    [Fact]
    public void PdhQuery_DiskAndNetworkCounters_CollectWithoutThrowing()
    {
        using var query = new PdhQuery();
        var diskTime = query.TryAddCounter(@"\PhysicalDisk(_Total)\% Disk Time");
        var netReceived = query.TryAddCounter(@"\Network Interface(*)\Bytes Received/sec");

        Assert.True(query.Collect());
        Thread.Sleep(200);
        Assert.True(query.Collect());

        if (diskTime is not null)
        {
            var value = diskTime.ReadDouble();
            if (value is not null)
            {
                Assert.True(value >= 0);
            }
        }

        if (netReceived is not null)
        {
            var items = netReceived.ReadArray();
            Assert.All(items, item => Assert.False(string.IsNullOrEmpty(item.Instance)));
        }
    }

    [Fact]
    public void GpuEngineCounter_WhenPresent_AggregatesBelow100PerAdapter()
    {
        using var query = new PdhQuery();
        var gpu = query.TryAddCounter(@"\GPU Engine(*)\Utilization Percentage");
        if (gpu is null)
        {
            return; // No GPU counters on this machine (e.g. bare CI VM); nothing to verify.
        }

        query.Collect();
        Thread.Sleep(200);
        query.Collect();

        var aggregated = GpuMetricsParser.AggregateUtilization(gpu.ReadArray());

        Assert.All(aggregated.Values, v => Assert.InRange(v, 0.0, 100.0));
    }

    [Fact]
    public void DxgiAdapters_EnumerateReturnsLuidKeysInPdhFormat()
    {
        var adapters = DxgiAdapters.Enumerate();

        Assert.All(adapters, a => Assert.Matches("^luid_0x[0-9A-F]{8}_0x[0-9A-F]{8}$", a.LuidKey));
    }
}
