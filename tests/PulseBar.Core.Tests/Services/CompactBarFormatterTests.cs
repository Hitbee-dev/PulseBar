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

    [Theory]
    [InlineData(50.0, BarSegmentKind.Value)]
    [InlineData(69.9, BarSegmentKind.Value)]
    [InlineData(70.0, BarSegmentKind.ValueWarning)]
    [InlineData(85.0, BarSegmentKind.ValueHigh)]
    [InlineData(95.0, BarSegmentKind.ValueCritical)]
    [InlineData(100.0, BarSegmentKind.ValueCritical)]
    [InlineData(null, BarSegmentKind.Value)]
    public void ClassifyPercent_UsesSpecThresholds(double? percent, BarSegmentKind expected)
    {
        Assert.Equal(expected, CompactBarFormatter.ClassifyPercent(percent, new ThresholdsConfig()));
    }

    [Fact]
    public void SystemSegments_HotCpu_GetsCriticalKind()
    {
        var segments = CompactBarFormatter.SystemSegments(
            Sample(cpu: 97), new MetricsConfig(), BarLayout.TwoLine, new ThresholdsConfig());

        var cpuValue = segments[1];
        Assert.Equal("97", cpuValue.Text);
        Assert.Equal(BarSegmentKind.ValueCritical, cpuValue.Kind);
        Assert.Equal(BarSegmentKind.Label, segments[0].Kind);
    }

    [Fact]
    public void SystemSegments_NetworkTokens_UseDownUpKinds()
    {
        var segments = CompactBarFormatter.SystemSegments(
            Sample(), new MetricsConfig(), BarLayout.TwoLine, new ThresholdsConfig());

        Assert.Contains(segments, s => s.Kind == BarSegmentKind.Down && s.Text.StartsWith('↓'));
        Assert.Contains(segments, s => s.Kind == BarSegmentKind.Up && s.Text.StartsWith('↑'));
    }

    [Fact]
    public void ProviderSegments_BrandNamesAndThresholdValues()
    {
        var snapshot = new UsageSnapshot(
            "claude", null, null,
            [
                new UsageWindow("claude:five-hour", "5h", 91, 9, TimeSpan.FromMinutes(300), null,
                    DataFreshness.Fresh, DataScope.ServerAccount),
                new UsageWindow("claude:seven-day", "Week", 33, 67, TimeSpan.FromMinutes(10080), null,
                    DataFreshness.Fresh, DataScope.ServerAccount),
            ],
            new Dictionary<string, TokenUsage>(), new Dictionary<string, TokenUsage>(),
            null, DateTimeOffset.Now, DataFreshness.Fresh, null, null);

        var segments = CompactBarFormatter.ProviderSegments([snapshot], k => k, new ThresholdsConfig());

        Assert.Equal(BarSegmentKind.ProviderClaude, segments[0].Kind);
        Assert.Contains(segments, s => s.Text == "91" && s.Kind == BarSegmentKind.ValueHigh);
        Assert.Contains(segments, s => s.Text == "33" && s.Kind == BarSegmentKind.Value);
        Assert.Equal(
            "Claude 5h 91 · W 33",
            string.Concat(segments.Select(s => s.Text)));
    }
}
