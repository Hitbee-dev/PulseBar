namespace PulseBar.Core.Models;

/// <summary>
/// One normalized model-request token event (from Claude Code OpenTelemetry).
/// EventKey is the idempotency key — re-ingesting the same event is a no-op.
/// Never contains prompt or response content.
/// </summary>
public sealed record TokenUsageEvent(
    string EventKey,
    string ProviderId,
    string ProfileId,
    string ModelId,
    DateTimeOffset OccurredAt,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheCreationTokens,
    double? EstimatedCostUsd);
