using Microsoft.Extensions.Logging.Abstractions;
using PulseBar.Core.Models;
using PulseBar.Providers.Claude;
using PulseBar.Providers.Claude.Statusline;

namespace PulseBar.Claude.Tests;

public sealed class ClaudeProviderTests : IDisposable
{
    private readonly string _dir;
    private readonly string _cachePath;

    public ClaudeProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PulseBarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _cachePath = Path.Combine(_dir, "claude-status.json");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteCache(DateTimeOffset updatedAt, double fiveHourPercent = 41, double sevenDayPercent = 68)
    {
        var cache = new ClaudeStatusCache
        {
            UpdatedAt = updatedAt,
            Model = new CachedModel { Id = "claude-fable-5", DisplayName = "Fable 5" },
            RateLimits = new CachedRateLimits
            {
                FiveHour = new CachedWindow { UsedPercent = fiveHourPercent, ResetsAt = 1784269200 },
                SevenDay = new CachedWindow { UsedPercent = sevenDayPercent, ResetsAt = 1784697600 },
            },
        };
        File.WriteAllText(_cachePath, cache.ToJson());
    }

    private async Task<UsageSnapshot> GetFirstSnapshotAsync(ClaudeProvider provider)
    {
        var received = new TaskCompletionSource<UsageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.SnapshotUpdated += (_, snapshot) => received.TrySetResult(snapshot);

        var profile = new ProviderProfile("claude-test", "claude", ExecutionEnvironmentType.Wsl, null, null, null, true);
        await provider.StartAsync(profile, CancellationToken.None);

        return await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FreshCache_ProducesFreshSnapshotWithBothWindows()
    {
        WriteCache(DateTimeOffset.Now.AddMinutes(-1));
        await using var provider = new ClaudeProvider(
            NullLogger<ClaudeProvider>.Instance,
            new ClaudeProviderOptions { CachePath = _cachePath });

        var snapshot = await GetFirstSnapshotAsync(provider);

        Assert.Equal(DataFreshness.Fresh, snapshot.Freshness);
        Assert.Equal(2, snapshot.Windows.Count);
        var five = snapshot.Windows.First(w => w.Duration == TimeSpan.FromMinutes(300));
        Assert.Equal(41.0, five.UsedPercent);
        Assert.Equal(DataScope.ServerAccount, five.Scope);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784269200), five.ResetsAt);
    }

    [Fact]
    public async Task OldCache_IsMarkedStale()
    {
        WriteCache(DateTimeOffset.Now.AddMinutes(-30));
        await using var provider = new ClaudeProvider(
            NullLogger<ClaudeProvider>.Instance,
            new ClaudeProviderOptions { CachePath = _cachePath });

        var snapshot = await GetFirstSnapshotAsync(provider);

        Assert.Equal(DataFreshness.Stale, snapshot.Freshness);
        Assert.All(snapshot.Windows, w => Assert.Equal(DataFreshness.Stale, w.Freshness));
    }

    [Fact]
    public async Task MissingCache_IsUnavailable()
    {
        await using var provider = new ClaudeProvider(
            NullLogger<ClaudeProvider>.Instance,
            new ClaudeProviderOptions { CachePath = _cachePath });

        var snapshot = await GetFirstSnapshotAsync(provider);

        Assert.Equal(DataFreshness.Unavailable, snapshot.Freshness);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public async Task CorruptCache_ReportsError()
    {
        File.WriteAllText(_cachePath, "{{{ nope");
        await using var provider = new ClaudeProvider(
            NullLogger<ClaudeProvider>.Instance,
            new ClaudeProviderOptions { CachePath = _cachePath });

        var snapshot = await GetFirstSnapshotAsync(provider);

        Assert.Equal(DataFreshness.Error, snapshot.Freshness);
    }
}
