using Microsoft.Extensions.Logging;
using PulseBar.Core.Configuration;
using PulseBar.Core.Models;
using PulseBar.Storage.Sqlite;

namespace PulseBar.App.Services;

public sealed record FableUsageSummary(TokenUsage Today, TokenUsage SevenDays);

/// <summary>
/// Aggregates locally collected Claude token events for the Fable model matcher.
/// This is *local telemetry from this PC's Claude Code* — it is never presented
/// as (nor comparable to) the server-side subscription quota (spec §22).
/// Queries run off the UI thread and results are cached.
/// </summary>
public sealed class FableUsageService
{
    private readonly ITokenUsageRepository _repository;
    private readonly IConfigurationService _config;
    private readonly ILogger<FableUsageService> _logger;
    private readonly object _gate = new();

    private FableUsageSummary _cached = new(TokenUsage.Zero, TokenUsage.Zero);

    public FableUsageService(
        ITokenUsageRepository repository,
        IConfigurationService config,
        ILogger<FableUsageService> logger)
    {
        _repository = repository;
        _config = config;
        _logger = logger;
    }

    public event EventHandler<FableUsageSummary>? Updated;

    public FableUsageSummary Current
    {
        get
        {
            lock (_gate)
            {
                return _cached;
            }
        }
    }

    /// <summary>Re-queries storage; call from a background thread (never the UI thread).</summary>
    public void Refresh()
    {
        try
        {
            var now = DateTimeOffset.Now;
            var todayStart = new DateTimeOffset(now.Date, now.Offset);
            var weekStart = todayStart.AddDays(-6);
            var matcher = _config.Current.Claude.FableModelMatcher;

            var today = Sum(_repository.GetUsageByModel("claude", todayStart, now.AddMinutes(1)), matcher);
            var week = Sum(_repository.GetUsageByModel("claude", weekStart, now.AddMinutes(1)), matcher);

            FableUsageSummary summary = new(today, week);
            lock (_gate)
            {
                _cached = summary;
            }

            Updated?.Invoke(this, summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fable usage aggregation failed.");
        }
    }

    /// <summary>Exact model-id match; a trailing '*' turns the matcher into a prefix.</summary>
    public static bool Matches(string modelId, string matcher)
        => matcher.EndsWith('*')
            ? modelId.StartsWith(matcher[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(modelId, matcher, StringComparison.OrdinalIgnoreCase);

    private static TokenUsage Sum(IReadOnlyDictionary<string, TokenUsage> byModel, string matcher)
    {
        var total = TokenUsage.Zero;
        foreach (var (model, usage) in byModel)
        {
            if (Matches(model, matcher))
            {
                total = total.Add(usage);
            }
        }

        return total;
    }
}
