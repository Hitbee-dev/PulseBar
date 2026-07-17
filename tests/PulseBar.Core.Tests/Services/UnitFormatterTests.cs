using PulseBar.Core.Services;

namespace PulseBar.Core.Tests.Services;

public class UnitFormatterTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(950, "950")]
    [InlineData(1024, "1K")]
    [InlineData(1536, "1.5K")]
    [InlineData(32 * 1024 * 1024, "32M")]
    [InlineData(9.8 * 1024 * 1024 * 1024, "9.8G")]
    [InlineData(-5, "0")]
    public void BytesCompact_FormatsHumanReadably(double bytes, string expected)
    {
        Assert.Equal(expected, UnitFormatter.BytesCompact(bytes));
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(2048, "2 KB/s")]
    [InlineData(32.5 * 1024 * 1024, "32 MB/s")]
    public void BytesPerSecond_IncludesUnit(double value, string expected)
    {
        Assert.Equal(expected, UnitFormatter.BytesPerSecond(value));
    }

    [Theory]
    [InlineData(14.3, "14")]
    [InlineData(99.6, "100")]
    [InlineData(0.0, "0")]
    [InlineData(null, "—")]
    public void Percent_RoundsOrShowsDash(double? value, string expected)
    {
        Assert.Equal(expected, UnitFormatter.Percent(value));
    }
}
