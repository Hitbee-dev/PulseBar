using PulseBar.Providers.Claude.OpenTelemetry;

namespace PulseBar.Claude.Tests;

public class OtelLogsParserTests
{
    private static string Payload(string logRecordsJson) => $$"""
        {"resourceLogs":[{"resource":{},"scopeLogs":[{"scope":{},"logRecords":[{{logRecordsJson}}]}]}]}
        """;

    private const string ApiRequestRecord = """
        {
          "timeUnixNano": "1784690000000000000",
          "body": {"stringValue": "claude_code.api_request"},
          "attributes": [
            {"key": "model", "value": {"stringValue": "claude-fable-5"}},
            {"key": "input_tokens", "value": {"stringValue": "640000"}},
            {"key": "output_tokens", "value": {"intValue": "91000"}},
            {"key": "cache_read_tokens", "value": {"stringValue": "1050000"}},
            {"key": "cache_creation_tokens", "value": {"stringValue": "39000"}},
            {"key": "cost_usd", "value": {"doubleValue": 1.25}},
            {"key": "request_id", "value": {"stringValue": "req_abc"}},
            {"key": "session.id", "value": {"stringValue": "sess-1"}}
          ]
        }
        """;

    [Fact]
    public void Parse_ApiRequestEvent_ExtractsAllTokenBuckets()
    {
        var events = OtelLogsParser.Parse(Payload(ApiRequestRecord), "test-profile");

        var e = Assert.Single(events);
        Assert.Equal("claude-fable-5", e.ModelId);
        Assert.Equal(640000, e.InputTokens);
        Assert.Equal(91000, e.OutputTokens);
        Assert.Equal(1050000, e.CacheReadTokens);
        Assert.Equal(39000, e.CacheCreationTokens);
        Assert.Equal(1.25, e.EstimatedCostUsd);
        Assert.Equal("req:req_abc", e.EventKey);
        Assert.Equal("claude", e.ProviderId);
        Assert.Equal("test-profile", e.ProfileId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1784690000000), e.OccurredAt);
    }

    [Fact]
    public void Parse_NonApiRequestRecords_AreSkipped()
    {
        var record = """
            {"body":{"stringValue":"claude_code.tool_result"},
             "attributes":[{"key":"model","value":{"stringValue":"claude-fable-5"}}]}
            """;

        Assert.Empty(OtelLogsParser.Parse(Payload(record), "p"));
    }

    [Fact]
    public void Parse_MissingRequestId_FallsBackToSessionAndTime()
    {
        var record = """
            {"timeUnixNano":"123456789000000",
             "body":{"stringValue":"claude_code.api_request"},
             "attributes":[
               {"key":"model","value":{"stringValue":"claude-fable-5"}},
               {"key":"session.id","value":{"stringValue":"sess-9"}}]}
            """;

        var e = Assert.Single(OtelLogsParser.Parse(Payload(record), "p"));

        Assert.Equal("ses:sess-9:123456789000000", e.EventKey);
    }

    [Fact]
    public void Parse_NoIdsAtAll_UsesContentHash()
    {
        var record = """
            {"body":{"stringValue":"claude_code.api_request"},
             "attributes":[{"key":"model","value":{"stringValue":"claude-fable-5"}},
                           {"key":"input_tokens","value":{"stringValue":"5"}}]}
            """;

        var e = Assert.Single(OtelLogsParser.Parse(Payload(record), "p"));

        Assert.StartsWith("hsh:", e.EventKey);
    }

    [Fact]
    public void Parse_SameEventTwice_ProducesSameKey()
    {
        var a = OtelLogsParser.Parse(Payload(ApiRequestRecord), "p").Single();
        var b = OtelLogsParser.Parse(Payload(ApiRequestRecord), "p").Single();

        Assert.Equal(a.EventKey, b.EventKey);
    }

    [Fact]
    public void Parse_EventNameFromAttribute_AlsoAccepted()
    {
        var record = """
            {"attributes":[
               {"key":"event.name","value":{"stringValue":"api_request"}},
               {"key":"model","value":{"stringValue":"claude-sonnet-5"}},
               {"key":"input_tokens","value":{"stringValue":"7"}}]}
            """;

        var e = Assert.Single(OtelLogsParser.Parse(Payload(record), "p"));
        Assert.Equal("claude-sonnet-5", e.ModelId);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"resourceLogs":[]}""")]
    public void Parse_EmptyOrInvalid_ReturnsNoEvents(string json)
    {
        Assert.Empty(OtelLogsParser.Parse(json, "p"));
    }
}
