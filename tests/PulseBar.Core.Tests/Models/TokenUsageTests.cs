using PulseBar.Core.Models;

namespace PulseBar.Core.Tests.Models;

public class TokenUsageTests
{
    [Fact]
    public void Create_SumsAllFourBuckets()
    {
        var usage = TokenUsage.Create(640_000, 91_000, 1_050_000, 39_000);

        Assert.Equal(1_820_000, usage.TotalTokens);
    }

    [Fact]
    public void Add_AccumulatesEveryBucket()
    {
        var a = TokenUsage.Create(10, 20, 30, 40);
        var b = TokenUsage.Create(1, 2, 3, 4);

        var sum = a.Add(b);

        Assert.Equal(11, sum.InputTokens);
        Assert.Equal(22, sum.OutputTokens);
        Assert.Equal(33, sum.CacheReadTokens);
        Assert.Equal(44, sum.CacheCreationTokens);
        Assert.Equal(110, sum.TotalTokens);
    }

    [Fact]
    public void Zero_IsAllZeroes()
    {
        Assert.Equal(0, TokenUsage.Zero.TotalTokens);
        Assert.Equal(TokenUsage.Zero, TokenUsage.Create(0, 0, 0, 0));
    }
}
