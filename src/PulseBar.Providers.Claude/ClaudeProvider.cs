using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Interfaces;
using PulseBar.Core.Models;
using PulseBar.Providers.Claude.Statusline;

namespace PulseBar.Providers.Claude;

public sealed class ClaudeProviderOptions
{
    public required string CachePath { get; init; }
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Values older than this are shown dimmed as "오래됨" (spec §2.2 A).</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Claude subscription quota provider. Reads the normalized cache file written by
/// the PulseBar.Bridge statusline command — it never talks to Claude servers and
/// never touches Claude credentials. Data updates only while Claude Code runs.
/// </summary>
public sealed class ClaudeProvider : IUsageProvider
{
    private readonly ILogger<ClaudeProvider> _logger;
    private readonly ClaudeProviderOptions _options;
    private readonly SemaphoreSlim _refreshSignal = new(0, int.MaxValue);

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private UsageSnapshot? _latest;
    private bool _hasPublished;
    private DateTimeOffset? _lastCacheUpdatedAt;
    private DataFreshness _lastFreshness;

    public ClaudeProvider(ILogger<ClaudeProvider> logger, ClaudeProviderOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public string Id => "claude";

    public string DisplayName => "Claude";

    public event EventHandler<UsageSnapshot>? SnapshotUpdated;

    public event EventHandler<ProviderStateChanged>? StateChanged;

    public async Task<ProviderCapability> ProbeAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            if (profile.Environment == ExecutionEnvironmentType.Wsl)
            {
                var args = new List<string>();
                if (!string.IsNullOrWhiteSpace(profile.WslDistribution))
                {
                    args.AddRange(["-d", profile.WslDistribution]);
                }

                args.AddRange(["--", "sh", "-lc", "command -v claude"]);
                var path = await RunCaptureAsync("wsl.exe", args, cancellationToken).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(path)
                    ? new ProviderCapability(false, null, null, "claude not found in WSL distribution")
                    : new ProviderCapability(true, path.Trim(), null, null);
            }

            var found = await RunCaptureAsync("where.exe", ["claude"], cancellationToken).ConfigureAwait(false);
            var first = found?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrWhiteSpace(first)
                ? new ProviderCapability(false, null, null, "claude not found on PATH")
                : new ProviderCapability(true, first, null, null);
        }
        catch (Exception ex)
        {
            return new ProviderCapability(false, null, null, ex.Message);
        }
    }

    public Task StartAsync(ProviderProfile profile, CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task<UsageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        => Task.FromResult(_latest ?? BuildUnavailableSnapshot());

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        _refreshSignal.Release();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            if (_runTask is not null)
            {
                try
                {
                    await _runTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            _cts.Dispose();
            _cts = null;
        }

        _refreshSignal.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ReadCacheAndPublish();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Claude cache read failed.");
            }

            try
            {
                var delay = Task.Delay(_options.PollInterval, cancellationToken);
                var refresh = _refreshSignal.WaitAsync(cancellationToken);
                await Task.WhenAny(delay, refresh).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void ReadCacheAndPublish()
    {
        if (!File.Exists(_options.CachePath))
        {
            PublishIfChanged(null, DataFreshness.Unavailable);
            return;
        }

        var cache = ClaudeStatusCache.FromJson(File.ReadAllText(_options.CachePath));
        if (cache is null)
        {
            PublishIfChanged(null, DataFreshness.Error);
            return;
        }

        var age = DateTimeOffset.Now - cache.UpdatedAt;
        var freshness = age <= _options.StaleAfter ? DataFreshness.Fresh : DataFreshness.Stale;
        PublishIfChanged(cache, freshness);
    }

    private void PublishIfChanged(ClaudeStatusCache? cache, DataFreshness freshness)
    {
        // Avoid re-raising identical snapshots every poll tick.
        if (_hasPublished && cache?.UpdatedAt == _lastCacheUpdatedAt && freshness == _lastFreshness)
        {
            return;
        }

        _hasPublished = true;
        _lastCacheUpdatedAt = cache?.UpdatedAt;
        _lastFreshness = freshness;

        if (cache is null)
        {
            _latest = BuildUnavailableSnapshot() with { Freshness = freshness };
            SnapshotUpdated?.Invoke(this, _latest);
            ChangeState(
                freshness == DataFreshness.Error
                    ? ProviderConnectionState.Error
                    : ProviderConnectionState.Disconnected,
                "statusline bridge cache not found");
            return;
        }

        var windows = new List<UsageWindow>(2);
        AddWindow(windows, cache.RateLimits?.FiveHour, "claude:five-hour", "5h", 300, freshness);
        AddWindow(windows, cache.RateLimits?.SevenDay, "claude:seven-day", "Week", 10080, freshness);

        _latest = new UsageSnapshot(
            ProviderId: Id,
            AccountLabel: null,
            Plan: null,
            Windows: windows,
            ModelUsageToday: new Dictionary<string, TokenUsage>(),
            ModelUsageSevenDays: new Dictionary<string, TokenUsage>(),
            CreditBalance: null,
            CollectedAt: cache.UpdatedAt,
            Freshness: freshness,
            ErrorCode: null,
            ErrorMessage: null);

        SnapshotUpdated?.Invoke(this, _latest);
        ChangeState(
            freshness == DataFreshness.Fresh ? ProviderConnectionState.Connected : ProviderConnectionState.Stale,
            null);
    }

    private static void AddWindow(
        List<UsageWindow> windows,
        CachedWindow? cached,
        string id,
        string displayName,
        int durationMins,
        DataFreshness freshness)
    {
        if (cached is null)
        {
            return;
        }

        windows.Add(new UsageWindow(
            Id: id,
            DisplayName: displayName,
            UsedPercent: cached.UsedPercent,
            RemainingPercent: cached.UsedPercent is { } used ? 100.0 - used : null,
            Duration: TimeSpan.FromMinutes(durationMins),
            ResetsAt: cached.ResetsAt is { } unix ? DateTimeOffset.FromUnixTimeSeconds(unix) : null,
            Freshness: freshness,
            Scope: DataScope.ServerAccount));
    }

    private UsageSnapshot BuildUnavailableSnapshot()
        => new(
            Id, null, null,
            [],
            new Dictionary<string, TokenUsage>(),
            new Dictionary<string, TokenUsage>(),
            null,
            DateTimeOffset.Now,
            DataFreshness.Unavailable,
            null,
            null);

    private void ChangeState(ProviderConnectionState state, string? message)
        => StateChanged?.Invoke(this, new ProviderStateChanged(Id, state, message));

    private static async Task<string?> RunCaptureAsync(
        string fileName,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0 ? output : null;
    }
}
