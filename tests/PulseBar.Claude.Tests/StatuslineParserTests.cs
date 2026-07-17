using PulseBar.Providers.Claude.Statusline;

namespace PulseBar.Claude.Tests;

public class StatuslineParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 6, 20, 0, TimeSpan.Zero);

    private const string FullPayload = """
        {
          "hook_event_name": "Status",
          "session_id": "abc-123",
          "model": {"id": "claude-fable-5", "display_name": "Fable 5"},
          "workspace": {"current_dir": "/secret/project"},
          "rate_limits": {
            "five_hour": {"used_percentage": 41.0, "resets_at": 1784269200},
            "seven_day": {"used_percentage": 68.0, "resets_at": 1784697600}
          }
        }
        """;

    [Fact]
    public void Parse_FullPayload_ExtractsModelAndBothWindows()
    {
        var cache = StatuslineParser.Parse(FullPayload, Now);

        Assert.NotNull(cache);
        Assert.Equal("claude-fable-5", cache!.Model!.Id);
        Assert.Equal("Fable 5", cache.Model.DisplayName);
        Assert.Equal(41.0, cache.RateLimits!.FiveHour!.UsedPercent);
        Assert.Equal(1784269200, cache.RateLimits.FiveHour.ResetsAt);
        Assert.Equal(68.0, cache.RateLimits.SevenDay!.UsedPercent);
        Assert.Equal(Now, cache.UpdatedAt);
    }

    [Fact]
    public void Parse_NeverStoresSessionOrWorkspaceData()
    {
        var cache = StatuslineParser.Parse(FullPayload, Now);
        var json = cache!.ToJson();

        Assert.DoesNotContain("abc-123", json);
        Assert.DoesNotContain("secret", json);
    }

    [Fact]
    public void Parse_MissingRateLimits_YieldsNullRateLimits()
    {
        var cache = StatuslineParser.Parse("""{"model":{"id":"claude-fable-5"}}""", Now);

        Assert.NotNull(cache);
        Assert.Null(cache!.RateLimits);
    }

    [Fact]
    public void Parse_OnlyFiveHour_SevenDayIsNull()
    {
        var cache = StatuslineParser.Parse(
            """{"rate_limits":{"five_hour":{"used_percentage":10,"resets_at":100}}}""", Now);

        Assert.NotNull(cache!.RateLimits!.FiveHour);
        Assert.Null(cache.RateLimits.SevenDay);
    }

    [Fact]
    public void Parse_IsoResetsAt_ConvertedToUnixSeconds()
    {
        var cache = StatuslineParser.Parse(
            """{"rate_limits":{"five_hour":{"used_percentage":10,"resets_at":"2026-07-17T06:00:00Z"}}}""", Now);

        Assert.Equal(
            new DateTimeOffset(2026, 7, 17, 6, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
            cache!.RateLimits!.FiveHour!.ResetsAt);
    }

    [Fact]
    public void Parse_OutOfRangePercent_IsClamped()
    {
        var cache = StatuslineParser.Parse(
            """{"rate_limits":{"five_hour":{"used_percentage":140,"resets_at":1}}}""", Now);

        Assert.Equal(100.0, cache!.RateLimits!.FiveHour!.UsedPercent);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void Parse_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(StatuslineParser.Parse(input, Now));
    }

    [Fact]
    public void CacheJson_RoundTrips()
    {
        var cache = StatuslineParser.Parse(FullPayload, Now);
        var restored = ClaudeStatusCache.FromJson(cache!.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(41.0, restored!.RateLimits!.FiveHour!.UsedPercent);
        Assert.Equal("claude", restored.Provider);
        Assert.Equal(1, restored.SchemaVersion);
    }
}
