using PulseBar.Core.Models;
using PulseBar.Storage.Sqlite;

namespace PulseBar.Core.Tests.Storage;

public sealed class SqliteTokenUsageRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteTokenUsageRepository _repository;

    public SqliteTokenUsageRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PulseBarTests", Guid.NewGuid().ToString("N"));
        _repository = new SqliteTokenUsageRepository(Path.Combine(_dir, "test.db"));
        _repository.Initialize();
    }

    public void Dispose()
    {
        _repository.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static TokenUsageEvent Event(
        string key,
        string model = "claude-fable-5",
        long input = 100,
        DateTimeOffset? at = null)
        => new(key, "claude", "test", model, at ?? DateTimeOffset.UtcNow, input, 10, 5, 2, null);

    [Fact]
    public void UpsertEvents_DuplicateKeys_AreIgnored()
    {
        Assert.Equal(1, _repository.UpsertEvents([Event("a")]));
        Assert.Equal(0, _repository.UpsertEvents([Event("a", input: 999)]));

        var usage = _repository.GetUsageByModel(
            "claude", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(100, usage["claude-fable-5"].InputTokens);
    }

    [Fact]
    public void GetUsageByModel_SumsPerModelWithinRange()
    {
        var now = DateTimeOffset.UtcNow;
        _repository.UpsertEvents(
        [
            Event("a", "claude-fable-5", 100, now),
            Event("b", "claude-fable-5", 200, now),
            Event("c", "claude-sonnet-5", 999, now),
            Event("old", "claude-fable-5", 5000, now.AddDays(-10)),
        ]);

        var usage = _repository.GetUsageByModel("claude", now.AddDays(-1), now.AddDays(1));

        Assert.Equal(300, usage["claude-fable-5"].InputTokens);
        Assert.Equal(999, usage["claude-sonnet-5"].InputTokens);
        Assert.Equal(300 + 20 + 10 + 4, usage["claude-fable-5"].TotalTokens);
    }

    [Fact]
    public void PruneOlderThan_RemovesOldEventsOnly()
    {
        var now = DateTimeOffset.UtcNow;
        _repository.UpsertEvents([Event("old", at: now.AddDays(-40)), Event("new", at: now)]);

        var pruned = _repository.PruneOlderThan(now.AddDays(-30));

        Assert.Equal(1, pruned);
        var usage = _repository.GetUsageByModel("claude", now.AddDays(-60), now.AddDays(1));
        Assert.Equal(100, usage["claude-fable-5"].InputTokens);
    }
}
