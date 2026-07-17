using Microsoft.Data.Sqlite;
using PulseBar.Core.Models;

namespace PulseBar.Storage.Sqlite;

public interface ITokenUsageRepository : IDisposable
{
    void Initialize();

    /// <summary>Idempotent: events whose key already exists are ignored. Returns inserted count.</summary>
    int UpsertEvents(IEnumerable<TokenUsageEvent> events);

    /// <summary>Per-model token sums for [from, to). Times are compared in UTC.</summary>
    IReadOnlyDictionary<string, TokenUsage> GetUsageByModel(
        string providerId,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive);

    int PruneOlderThan(DateTimeOffset cutoff);
}

/// <summary>SQLite-backed token event store (schema per spec §11.2, WAL mode).</summary>
public sealed class SqliteTokenUsageRepository : ITokenUsageRepository
{
    private readonly SqliteConnection _connection;

    public SqliteTokenUsageRepository(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        _connection.Open();
    }

    public void Initialize()
    {
        Execute("PRAGMA journal_mode=WAL;");
        Execute("""
            CREATE TABLE IF NOT EXISTS token_usage_events (
                event_key TEXT PRIMARY KEY,
                provider_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                input_tokens INTEGER NOT NULL DEFAULT 0,
                output_tokens INTEGER NOT NULL DEFAULT 0,
                cache_read_tokens INTEGER NOT NULL DEFAULT 0,
                cache_creation_tokens INTEGER NOT NULL DEFAULT 0,
                estimated_cost_usd REAL NULL
            );
            """);
        Execute("""
            CREATE INDEX IF NOT EXISTS ix_token_usage_model_time
            ON token_usage_events(provider_id, profile_id, model_id, occurred_at_utc);
            """);
    }

    public int UpsertEvents(IEnumerable<TokenUsageEvent> events)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO token_usage_events
                (event_key, provider_id, profile_id, model_id, occurred_at_utc,
                 input_tokens, output_tokens, cache_read_tokens, cache_creation_tokens,
                 estimated_cost_usd)
            VALUES ($key, $provider, $profile, $model, $at,
                    $input, $output, $cacheRead, $cacheCreation, $cost);
            """;

        var pKey = command.Parameters.Add("$key", SqliteType.Text);
        var pProvider = command.Parameters.Add("$provider", SqliteType.Text);
        var pProfile = command.Parameters.Add("$profile", SqliteType.Text);
        var pModel = command.Parameters.Add("$model", SqliteType.Text);
        var pAt = command.Parameters.Add("$at", SqliteType.Text);
        var pInput = command.Parameters.Add("$input", SqliteType.Integer);
        var pOutput = command.Parameters.Add("$output", SqliteType.Integer);
        var pCacheRead = command.Parameters.Add("$cacheRead", SqliteType.Integer);
        var pCacheCreation = command.Parameters.Add("$cacheCreation", SqliteType.Integer);
        var pCost = command.Parameters.Add("$cost", SqliteType.Real);

        var inserted = 0;
        foreach (var e in events)
        {
            pKey.Value = e.EventKey;
            pProvider.Value = e.ProviderId;
            pProfile.Value = e.ProfileId;
            pModel.Value = e.ModelId;
            pAt.Value = e.OccurredAt.ToUniversalTime().ToString("O");
            pInput.Value = e.InputTokens;
            pOutput.Value = e.OutputTokens;
            pCacheRead.Value = e.CacheReadTokens;
            pCacheCreation.Value = e.CacheCreationTokens;
            pCost.Value = e.EstimatedCostUsd is { } cost ? cost : DBNull.Value;
            inserted += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return inserted;
    }

    public IReadOnlyDictionary<string, TokenUsage> GetUsageByModel(
        string providerId,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT model_id,
                   SUM(input_tokens), SUM(output_tokens),
                   SUM(cache_read_tokens), SUM(cache_creation_tokens)
            FROM token_usage_events
            WHERE provider_id = $provider
              AND occurred_at_utc >= $from AND occurred_at_utc < $to
            GROUP BY model_id;
            """;
        command.Parameters.AddWithValue("$provider", providerId);
        command.Parameters.AddWithValue("$from", fromInclusive.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$to", toExclusive.ToUniversalTime().ToString("O"));

        var result = new Dictionary<string, TokenUsage>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = TokenUsage.Create(
                reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
        }

        return result;
    }

    public int PruneOlderThan(DateTimeOffset cutoff)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM token_usage_events WHERE occurred_at_utc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoff.ToUniversalTime().ToString("O"));
        return command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
