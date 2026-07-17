namespace PulseBar.Core.Models;

/// <summary>One rate-limit window (e.g. five-hour, seven-day) as reported by a provider.</summary>
public sealed record UsageWindow(
    string Id,
    string DisplayName,
    double? UsedPercent,
    double? RemainingPercent,
    TimeSpan? Duration,
    DateTimeOffset? ResetsAt,
    DataFreshness Freshness,
    DataScope Scope);

/// <summary>Token counts for one model. TotalTokens is the plain sum of all four buckets.</summary>
public sealed record TokenUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheCreationTokens,
    long TotalTokens)
{
    public static TokenUsage Create(
        long inputTokens,
        long outputTokens,
        long cacheReadTokens,
        long cacheCreationTokens)
        => new(
            inputTokens,
            outputTokens,
            cacheReadTokens,
            cacheCreationTokens,
            inputTokens + outputTokens + cacheReadTokens + cacheCreationTokens);

    public static readonly TokenUsage Zero = new(0, 0, 0, 0, 0);

    public TokenUsage Add(TokenUsage other)
        => new(
            InputTokens + other.InputTokens,
            OutputTokens + other.OutputTokens,
            CacheReadTokens + other.CacheReadTokens,
            CacheCreationTokens + other.CacheCreationTokens,
            TotalTokens + other.TotalTokens);
}

public sealed record UsageSnapshot(
    string ProviderId,
    string? AccountLabel,
    string? Plan,
    IReadOnlyList<UsageWindow> Windows,
    IReadOnlyDictionary<string, TokenUsage> ModelUsageToday,
    IReadOnlyDictionary<string, TokenUsage> ModelUsageSevenDays,
    decimal? CreditBalance,
    DateTimeOffset CollectedAt,
    DataFreshness Freshness,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record ProviderStateChanged(
    string ProviderId,
    ProviderConnectionState State,
    string? Message);

/// <summary>Result of probing a profile for a usable CLI.</summary>
public sealed record ProviderCapability(
    bool Available,
    string? ExecutablePath,
    string? Version,
    string? Detail);
