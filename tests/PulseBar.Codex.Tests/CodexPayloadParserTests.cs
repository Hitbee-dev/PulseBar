using System.Text.Json;
using PulseBar.Core.Models;
using PulseBar.Providers.Codex.AppServer;

namespace PulseBar.Codex.Tests;

public class CodexPayloadParserTests
{
    // Shapes captured from a real `codex app-server` (codex-cli 0.144.4).
    private const string RateLimitsFixture = """
        {
          "rateLimits": {
            "limitId": "codex", "limitName": null,
            "primary": {"usedPercent": 12, "windowDurationMins": 10080, "resetsAt": 1784780185},
            "secondary": null,
            "credits": {"hasCredits": false, "unlimited": false, "balance": "0"},
            "planType": "pro"
          },
          "rateLimitsByLimitId": {
            "codex_spark": {
              "limitId": "codex_spark", "limitName": "Spark",
              "primary": {"usedPercent": 0, "windowDurationMins": 10080, "resetsAt": 1784879657},
              "secondary": null, "credits": null, "planType": "pro"
            },
            "codex": {
              "limitId": "codex", "limitName": null,
              "primary": {"usedPercent": 12, "windowDurationMins": 10080, "resetsAt": 1784780185},
              "secondary": {"usedPercent": 45.5, "windowDurationMins": 300, "resetsAt": 1784700000},
              "credits": {"hasCredits": true, "unlimited": false, "balance": "12.50"},
              "planType": "pro"
            }
          }
        }
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData(300, "five-hour")]
    [InlineData(10080, "seven-day")]
    [InlineData(null, "unknown")]
    [InlineData(1440, "custom-1440")]
    public void ClassifyWindow_UsesDurationNotNames(int? mins, string expected)
    {
        Assert.Equal(expected, CodexPayloadParser.ClassifyWindow(mins));
    }

    [Fact]
    public void ParseAccount_LoggedIn()
    {
        var result = Parse("""{"account":{"type":"chatgpt","email":"a@b.c","planType":"pro"},"requiresOpenaiAuth":true}""");

        var account = CodexPayloadParser.ParseAccount(result);

        Assert.True(account.IsLoggedIn);
        Assert.Equal("a@b.c", account.Email);
        Assert.Equal("pro", account.PlanType);
    }

    [Fact]
    public void ParseAccount_NullAccount_MeansLoginRequired()
    {
        var account = CodexPayloadParser.ParseAccount(Parse("""{"account":null}"""));

        Assert.False(account.IsLoggedIn);
    }

    [Fact]
    public void ParseRateLimits_PrefersByLimitIdOverSingle()
    {
        var parsed = CodexPayloadParser.ParseRateLimits(Parse(RateLimitsFixture));

        // 2 entries in byLimitId: codex has primary+secondary, spark has primary → 3 windows.
        Assert.Equal(3, parsed.Windows.Count);
        Assert.Contains(parsed.Windows, w => w.Id == "codex:secondary");
        Assert.Contains(parsed.Windows, w => w.Id == "codex_spark:primary");
    }

    [Fact]
    public void ParseRateLimits_FiveHourWindowSortsFirst()
    {
        var parsed = CodexPayloadParser.ParseRateLimits(Parse(RateLimitsFixture));

        Assert.Equal(TimeSpan.FromMinutes(300), parsed.Windows[0].Duration);
        Assert.Equal(45.5, parsed.Windows[0].UsedPercent);
        Assert.Equal(100.0 - 45.5, parsed.Windows[0].RemainingPercent);
    }

    [Fact]
    public void ParseRateLimits_ResetsAtIsUnixSeconds()
    {
        var parsed = CodexPayloadParser.ParseRateLimits(Parse(RateLimitsFixture));
        var week = parsed.Windows.First(w => w.Id == "codex:primary");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784780185), week.ResetsAt);
        Assert.Equal(DataScope.ServerAccount, week.Scope);
    }

    [Fact]
    public void ParseRateLimits_ReadsCreditBalance()
    {
        var parsed = CodexPayloadParser.ParseRateLimits(Parse(RateLimitsFixture));

        Assert.Equal(12.50m, parsed.CreditBalance);
    }

    [Fact]
    public void ParseRateLimits_SingleRateLimitsOnly_StillParses()
    {
        var json = """
            {"rateLimits":{"limitId":"codex","primary":{"usedPercent":25,"windowDurationMins":300,"resetsAt":1784269200}}}
            """;

        var parsed = CodexPayloadParser.ParseRateLimits(Parse(json));

        var window = Assert.Single(parsed.Windows);
        Assert.Equal("codex:primary", window.Id);
        Assert.Equal(25.0, window.UsedPercent);
    }

    [Fact]
    public void ParseRateLimits_EmptyPayload_ReturnsNoWindows()
    {
        var parsed = CodexPayloadParser.ParseRateLimits(Parse("{}"));

        Assert.Empty(parsed.Windows);
        Assert.Null(parsed.CreditBalance);
    }

    [Fact]
    public void ParseRateLimits_OutOfRangePercent_ClampedAndRecorded()
    {
        var json = """
            {"rateLimits":{"limitId":"x","primary":{"usedPercent":130,"windowDurationMins":300,"resetsAt":1}}}
            """;

        var parsed = CodexPayloadParser.ParseRateLimits(Parse(json));

        Assert.Equal(100.0, parsed.Windows[0].UsedPercent);
        Assert.Single(parsed.Anomalies);
    }

    [Fact]
    public void ParseRateLimits_MissingUsedPercent_IsNull()
    {
        var json = """
            {"rateLimits":{"limitId":"x","primary":{"windowDurationMins":300,"resetsAt":1}}}
            """;

        var parsed = CodexPayloadParser.ParseRateLimits(Parse(json));

        Assert.Null(parsed.Windows[0].UsedPercent);
        Assert.Null(parsed.Windows[0].RemainingPercent);
    }

    [Fact]
    public void ParseUsage_SumsTodayAndSevenDays()
    {
        var today = new DateOnly(2026, 7, 17);
        var json = """
            {
              "summary": {"lifetimeTokens": 5451211518, "peakDailyTokens": 562453426},
              "dailyUsageBuckets": [
                {"startDate": "2026-07-10", "tokens": 100},
                {"startDate": "2026-07-11", "tokens": 200},
                {"startDate": "2026-07-17", "tokens": 300}
              ]
            }
            """;

        var usage = CodexPayloadParser.ParseUsage(Parse(json), today);

        Assert.Equal(5451211518, usage.LifetimeTokens);
        Assert.Equal(300, usage.TodayTokens);
        Assert.Equal(500, usage.SevenDayTokens); // 07-11 .. 07-17 (07-10 is 7 days back, excluded)
    }

    [Fact]
    public void ParseUsage_EmptyPayload_IsAllZero()
    {
        var usage = CodexPayloadParser.ParseUsage(Parse("{}"), new DateOnly(2026, 7, 17));

        Assert.Null(usage.LifetimeTokens);
        Assert.Equal(0, usage.TodayTokens);
    }
}
