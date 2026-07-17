using PulseBar.Windows.Metrics;

namespace PulseBar.Windows.Tests.Metrics;

public class GpuMetricsParserTests
{
    private const string Luid = "luid_0x00000000_0x0000C6C7";

    [Fact]
    public void ParseEngineInstance_TypicalName_ExtractsLuidAndEngineType()
    {
        var parsed = GpuMetricsParser.ParseEngineInstance(
            $"pid_1234_{Luid}_phys_0_engtype_3D");

        Assert.NotNull(parsed);
        Assert.Equal(Luid, parsed!.Value.LuidKey);
        Assert.Equal("3D", parsed.Value.EngineType);
    }

    [Fact]
    public void ParseEngineInstance_EngineTypeWithUnderscore_KeepsFullSuffix()
    {
        var parsed = GpuMetricsParser.ParseEngineInstance(
            $"pid_42_{Luid}_phys_0_engtype_Video_Decode");

        Assert.Equal("Video_Decode", parsed!.Value.EngineType);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("pid_1_luid_x_y_engtype_3D")]
    [InlineData("pid_1_luid_0x0_0x0")]
    public void ParseEngineInstance_MalformedNames_ReturnNull(string instance)
    {
        Assert.Null(GpuMetricsParser.ParseEngineInstance(instance));
    }

    [Fact]
    public void ParseMemoryInstance_ExtractsLuid()
    {
        Assert.Equal(Luid, GpuMetricsParser.ExtractLuidKey($"{Luid}_phys_0"));
    }

    [Fact]
    public void AggregateUtilization_SumsPerEngineType_TakesBusiestEngine()
    {
        var values = new (string, double)[]
        {
            ($"pid_1_{Luid}_phys_0_engtype_3D", 30),
            ($"pid_2_{Luid}_phys_0_engtype_3D", 40),   // 3D total = 70
            ($"pid_1_{Luid}_phys_0_engtype_Copy", 10), // Copy total = 10
        };

        var result = GpuMetricsParser.AggregateUtilization(values);

        Assert.Equal(70.0, result[Luid], precision: 5);
    }

    [Fact]
    public void AggregateUtilization_NeverExceeds100()
    {
        var values = new (string, double)[]
        {
            ($"pid_1_{Luid}_phys_0_engtype_3D", 80),
            ($"pid_2_{Luid}_phys_0_engtype_3D", 90),
        };

        var result = GpuMetricsParser.AggregateUtilization(values);

        Assert.Equal(100.0, result[Luid]);
    }

    [Fact]
    public void AggregateUtilization_GroupsByAdapterLuid()
    {
        const string otherLuid = "luid_0x00000000_0x0000AAAA";
        var values = new (string, double)[]
        {
            ($"pid_1_{Luid}_phys_0_engtype_3D", 20),
            ($"pid_1_{otherLuid}_phys_0_engtype_3D", 60),
        };

        var result = GpuMetricsParser.AggregateUtilization(values);

        Assert.Equal(20.0, result[Luid], precision: 5);
        Assert.Equal(60.0, result[otherLuid], precision: 5);
    }

    [Fact]
    public void AggregateDedicatedMemory_SumsPerAdapter()
    {
        var values = new (string, double)[]
        {
            ($"{Luid}_phys_0", 4_000_000_000),
        };

        var result = GpuMetricsParser.AggregateDedicatedMemory(values);

        Assert.Equal(4_000_000_000, result[Luid], precision: 0);
    }
}
