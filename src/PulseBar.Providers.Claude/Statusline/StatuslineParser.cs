using System.Globalization;
using System.Text.Json;

namespace PulseBar.Providers.Claude.Statusline;

/// <summary>
/// Extracts the fields PulseBar needs from the official Claude Code statusline
/// input JSON. Everything is optional — missing fields yield nulls, and prompts,
/// transcripts and workspace contents are never read or stored.
/// </summary>
public static class StatuslineParser
{
    public static ClaudeStatusCache? Parse(string json, DateTimeOffset now)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var cache = new ClaudeStatusCache { UpdatedAt = now };

            if (root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
            {
                cache.Model = new CachedModel
                {
                    Id = GetString(model, "id"),
                    DisplayName = GetString(model, "display_name") ?? GetString(model, "displayName"),
                };
            }

            if (root.TryGetProperty("rate_limits", out var rateLimits)
                && rateLimits.ValueKind == JsonValueKind.Object)
            {
                cache.RateLimits = new CachedRateLimits
                {
                    FiveHour = ParseWindow(rateLimits, "five_hour"),
                    SevenDay = ParseWindow(rateLimits, "seven_day"),
                };
            }

            return cache;
        }
    }

    private static CachedWindow? ParseWindow(JsonElement rateLimits, string name)
    {
        if (!rateLimits.TryGetProperty(name, out var window)
            || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new CachedWindow
        {
            UsedPercent = GetPercent(window),
            ResetsAt = GetResetsAt(window),
        };
    }

    private static double? GetPercent(JsonElement window)
    {
        foreach (var key in (string[])["used_percentage", "used_percent", "usedPercentage"])
        {
            if (window.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return Math.Clamp(value.GetDouble(), 0.0, 100.0);
            }
        }

        return null;
    }

    private static long? GetResetsAt(JsonElement window)
    {
        if (!window.TryGetProperty("resets_at", out var value)
            && !window.TryGetProperty("resetsAt", out value))
        {
            return null;
        }

        // Observed as unix seconds; ISO-8601 strings are accepted defensively.
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unixSeconds))
        {
            return unixSeconds;
        }

        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return parsed.ToUnixTimeSeconds();
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
