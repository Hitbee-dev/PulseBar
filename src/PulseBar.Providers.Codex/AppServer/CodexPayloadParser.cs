using System.Globalization;
using System.Text.Json;
using PulseBar.Core.Models;

namespace PulseBar.Providers.Codex.AppServer;

public sealed record CodexAccount(string? Email, string? PlanType, bool IsLoggedIn);

public sealed record CodexRateLimits(
    IReadOnlyList<UsageWindow> Windows,
    decimal? CreditBalance,
    IReadOnlyList<string> Anomalies);

public sealed record CodexUsage(long? LifetimeTokens, long TodayTokens, long SevenDayTokens);

/// <summary>
/// Pure parsers for codex app-server payloads (verified against codex-cli 0.144.4).
/// Bucket names are never hardcoded: windows are classified by windowDurationMins.
/// Everything is null-safe — a missing bucket is normal, not an error.
/// </summary>
public static class CodexPayloadParser
{
    public static string ClassifyWindow(int? windowDurationMins)
        => windowDurationMins switch
        {
            300 => "five-hour",
            10080 => "seven-day",
            null => "unknown",
            _ => $"custom-{windowDurationMins}",
        };

    /// <summary>account/read → account info; a null account object means "login required".</summary>
    public static CodexAccount ParseAccount(JsonElement result)
    {
        if (!result.TryGetProperty("account", out var account)
            || account.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new CodexAccount(null, null, IsLoggedIn: false);
        }

        var email = account.TryGetProperty("email", out var e) ? e.GetString() : null;
        var plan = account.TryGetProperty("planType", out var p) ? p.GetString() : null;
        return new CodexAccount(email, plan, IsLoggedIn: true);
    }

    /// <summary>
    /// account/rateLimits/read → usage windows. rateLimitsByLimitId takes priority
    /// over the single rateLimits object when present.
    /// </summary>
    public static CodexRateLimits ParseRateLimits(JsonElement result)
    {
        var windows = new List<UsageWindow>();
        var anomalies = new List<string>();
        decimal? creditBalance = null;

        if (result.TryGetProperty("rateLimitsByLimitId", out var byId)
            && byId.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in byId.EnumerateObject())
            {
                ParseLimitEntry(entry.Value, entry.Name, windows, anomalies, ref creditBalance);
            }
        }
        else if (result.TryGetProperty("rateLimits", out var single)
                 && single.ValueKind == JsonValueKind.Object)
        {
            var limitId = single.TryGetProperty("limitId", out var lid) ? lid.GetString() ?? "default" : "default";
            ParseLimitEntry(single, limitId, windows, anomalies, ref creditBalance);
        }

        // Stable presentation order: five-hour first, then seven-day, then the rest.
        var ordered = windows
            .OrderBy(w => w.Duration?.TotalMinutes switch { 300 => 0, 10080 => 1, _ => 2 })
            .ThenBy(w => w.Id, StringComparer.Ordinal)
            .ToList();

        return new CodexRateLimits(ordered, creditBalance, anomalies);
    }

    /// <summary>account/usage/read → lifetime + today/7-day token activity (quota와 별개).</summary>
    public static CodexUsage ParseUsage(JsonElement result, DateOnly today)
    {
        long? lifetime = null;
        if (result.TryGetProperty("summary", out var summary)
            && summary.TryGetProperty("lifetimeTokens", out var lt)
            && lt.TryGetInt64(out var lifetimeValue))
        {
            lifetime = lifetimeValue;
        }

        long todayTokens = 0;
        long sevenDayTokens = 0;
        var weekStart = today.AddDays(-6);

        if (result.TryGetProperty("dailyUsageBuckets", out var buckets)
            && buckets.ValueKind == JsonValueKind.Array)
        {
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (!bucket.TryGetProperty("startDate", out var dateElement)
                    || !DateOnly.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, out var date)
                    || !bucket.TryGetProperty("tokens", out var tokensElement)
                    || !tokensElement.TryGetInt64(out var tokens))
                {
                    continue;
                }

                if (date == today)
                {
                    todayTokens += tokens;
                }

                if (date >= weekStart && date <= today)
                {
                    sevenDayTokens += tokens;
                }
            }
        }

        return new CodexUsage(lifetime, todayTokens, sevenDayTokens);
    }

    private static void ParseLimitEntry(
        JsonElement entry,
        string limitId,
        List<UsageWindow> windows,
        List<string> anomalies,
        ref decimal? creditBalance)
    {
        var limitName = entry.TryGetProperty("limitName", out var ln) ? ln.GetString() : null;

        AddWindow(entry, "primary", limitId, limitName, windows, anomalies);
        AddWindow(entry, "secondary", limitId, limitName, windows, anomalies);

        if (creditBalance is null
            && entry.TryGetProperty("credits", out var credits)
            && credits.ValueKind == JsonValueKind.Object
            && credits.TryGetProperty("balance", out var balance)
            && decimal.TryParse(balance.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            creditBalance = parsed;
        }
    }

    private static void AddWindow(
        JsonElement entry,
        string slot,
        string limitId,
        string? limitName,
        List<UsageWindow> windows,
        List<string> anomalies)
    {
        if (!entry.TryGetProperty(slot, out var window)
            || window.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        double? usedPercent = null;
        if (window.TryGetProperty("usedPercent", out var up) && up.ValueKind == JsonValueKind.Number)
        {
            var raw = up.GetDouble();
            if (raw is < 0 or > 100)
            {
                anomalies.Add($"{limitId}/{slot}: usedPercent out of range ({raw}).");
            }

            usedPercent = Math.Clamp(raw, 0.0, 100.0);
        }

        int? durationMins = null;
        if (window.TryGetProperty("windowDurationMins", out var wd)
            && wd.ValueKind == JsonValueKind.Number
            && wd.TryGetInt32(out var mins))
        {
            durationMins = mins;
        }

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resetsAt", out var ra)
            && ra.ValueKind == JsonValueKind.Number
            && ra.TryGetInt64(out var unixSeconds))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        var classification = ClassifyWindow(durationMins);
        windows.Add(new UsageWindow(
            Id: $"{limitId}:{slot}",
            DisplayName: string.IsNullOrWhiteSpace(limitName) ? $"{limitId} ({classification})" : limitName,
            UsedPercent: usedPercent,
            RemainingPercent: usedPercent is { } used ? 100.0 - used : null,
            Duration: durationMins is { } d ? TimeSpan.FromMinutes(d) : null,
            ResetsAt: resetsAt,
            Freshness: DataFreshness.Fresh,
            Scope: DataScope.ServerAccount));
    }
}
