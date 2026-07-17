using PulseBar.Core.Configuration;
using PulseBar.Core.Models;
using PulseBar.Core.Services;

namespace PulseBar.Core.Tests.Services;

public class CompactBarFormatterTests
{
    private static SystemMetrics Sample(
        double? cpu = 14,
        double? mem = 47,
        double? gpu = 82,
        ulong? vramUsed = 10_523_190_231, // ≈9.8G
        double? disk = 4,
        double? rx = 32 * 1024 * 1024,
        double? tx = 2 * 1024 * 1024)
        => new(
            cpu, mem, 32UL << 30, 15UL << 30,
            gpu, "GPU", vramUsed, 16UL << 30,
            disk, 1000, 2000, rx, tx,
            DateTimeOffset.UtcNow);

    [Fact]
    public void SystemLine_TwoLine_MatchesSpecFormat()
    {
        var line = CompactBarFormatter.SystemLine(Sample(), new MetricsConfig(), BarLayout.TwoLine);

        Assert.Equal("CPU 14  RAM 47  GPU 82  VRAM 9.8G  D 4  ↓32M ↑2M", line);
    }

    [Fact]
    public void SystemLine_UltraCompact_UsesShortTokens()
    {
        var line = CompactBarFormatter.SystemLine(Sample(), new MetricsConfig(), BarLayout.UltraCompact);

        Assert.Equal("C14 M47 G82 V9.8G D4 ↓32M ↑2M", line);
    }

    [Fact]
    public void SystemLine_UnavailableValues_ShowDash()
    {
        var metrics = Sample(cpu: null, gpu: null, vramUsed: null, rx: null, tx: null);

        var line = CompactBarFormatter.SystemLine(metrics, new MetricsConfig(), BarLayout.TwoLine);

        Assert.Contains("CPU —", line);
        Assert.Contains("GPU —", line);
        Assert.Contains("VRAM —", line);
        Assert.Contains("↓— ↑—", line);
    }

    [Fact]
    public void SystemLine_DisabledItems_AreOmitted()
    {
        var config = new MetricsConfig { ShowGpu = false, ShowVram = false, ShowNetwork = false };

        var line = CompactBarFormatter.SystemLine(Sample(), config, BarLayout.TwoLine);

        Assert.Equal("CPU 14  RAM 47  D 4", line);
    }
}
