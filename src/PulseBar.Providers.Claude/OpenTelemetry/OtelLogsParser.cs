using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PulseBar.Core.Models;

namespace PulseBar.Providers.Claude.OpenTelemetry;

/// <summary>
/// Extracts claude_code.api_request events from an OTLP/HTTP JSON logs payload
/// (ExportLogsServiceRequest). Only token counts and request metadata are read —
/// prompt/response bodies are never collected (and never requested from Claude).
/// OTLP JSON encodes int64 values as strings; both encodings are accepted.
/// </summary>
public static class OtelLogsParser
{
    public static IReadOnlyList<TokenUsageEvent> Parse(string json, string profileId)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            var events = new List<TokenUsageEvent>();
            if (!document.RootElement.TryGetProperty("resourceLogs", out var resourceLogs)
                || resourceLogs.ValueKind != JsonValueKind.Array)
            {
                return events;
            }

            foreach (var resourceLog in resourceLogs.EnumerateArray())
            {
                if (!resourceLog.TryGetProperty("scopeLogs", out var scopeLogs)
                    || scopeLogs.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var scopeLog in scopeLogs.EnumerateArray())
                {
                    if (!scopeLog.TryGetProperty("logRecords", out var records)
                        || records.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var record in records.EnumerateArray())
                    {
                        if (ParseRecord(record, profileId) is { } tokenEvent)
                        {
                            events.Add(tokenEvent);
                        }
                    }
                }
            }

            return events;
        }
    }

    private static TokenUsageEvent? ParseRecord(JsonElement record, string profileId)
    {
        var attributes = ReadAttributes(record);

        var eventName = GetBodyString(record) ?? attributes.GetValueOrDefault("event.name") ?? "";
        if (!eventName.Contains("api_request", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var model = attributes.GetValueOrDefault("model");
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var occurredAt = ParseTime(record);
        var input = ParseLong(attributes.GetValueOrDefault("input_tokens"));
        var output = ParseLong(attributes.GetValueOrDefault("output_tokens"));
        var cacheRead = ParseLong(attributes.GetValueOrDefault("cache_read_tokens"));
        var cacheCreation = ParseLong(attributes.GetValueOrDefault("cache_creation_tokens"));
        var cost = ParseDouble(attributes.GetValueOrDefault("cost_usd"));

        var eventKey = BuildEventKey(attributes, record, model, input, output, cacheRead, cacheCreation);

        return new TokenUsageEvent(
            eventKey,
            "claude",
            profileId,
            model,
            occurredAt,
            input,
            output,
            cacheRead,
            cacheCreation,
            cost);
    }

    /// <summary>request_id → session.id+time → content hash (spec §10.7 dedup order).</summary>
    private static string BuildEventKey(
        Dictionary<string, string> attributes,
        JsonElement record,
        string model,
        long input,
        long output,
        long cacheRead,
        long cacheCreation)
    {
        var requestId = attributes.GetValueOrDefault("request_id") ?? attributes.GetValueOrDefault("requestId");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            return $"req:{requestId}";
        }

        var time = GetTimeNano(record);
        var sessionId = attributes.GetValueOrDefault("session.id");
        if (!string.IsNullOrWhiteSpace(sessionId) && time is not null)
        {
            return $"ses:{sessionId}:{time}";
        }

        var payload = $"{time}|{model}|{input}|{output}|{cacheRead}|{cacheCreation}";
        return "hsh:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..32];
    }

    private static Dictionary<string, string> ReadAttributes(JsonElement record)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!record.TryGetProperty("attributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var attribute in attributes.EnumerateArray())
        {
            if (!attribute.TryGetProperty("key", out var key)
                || key.ValueKind != JsonValueKind.String
                || !attribute.TryGetProperty("value", out var value))
            {
                continue;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.Object when value.TryGetProperty("stringValue", out var s)
                    && s.ValueKind == JsonValueKind.String => s.GetString(),
                JsonValueKind.Object when value.TryGetProperty("intValue", out var i)
                    => i.ValueKind == JsonValueKind.String ? i.GetString() : i.GetRawText(),
                JsonValueKind.Object when value.TryGetProperty("doubleValue", out var d)
                    => d.GetRawText(),
                _ => null,
            };

            if (text is not null)
            {
                result[key.GetString()!] = text;
            }
        }

        return result;
    }

    private static string? GetBodyString(JsonElement record)
        => record.TryGetProperty("body", out var body)
           && body.ValueKind == JsonValueKind.Object
           && body.TryGetProperty("stringValue", out var s)
           && s.ValueKind == JsonValueKind.String
            ? s.GetString()
            : null;

    private static string? GetTimeNano(JsonElement record)
    {
        foreach (var field in (string[])["timeUnixNano", "observedTimeUnixNano"])
        {
            if (record.TryGetProperty(field, out var value))
            {
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
                if (!string.IsNullOrWhiteSpace(text) && text != "0")
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset ParseTime(JsonElement record)
    {
        var nano = GetTimeNano(record);
        if (nano is not null && ulong.TryParse(nano, out var nanoValue) && nanoValue > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(nanoValue / 1_000_000UL));
        }

        return DateTimeOffset.UtcNow;
    }

    private static long ParseLong(string? text)
        => long.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 0;

    private static double? ParseDouble(string? text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
