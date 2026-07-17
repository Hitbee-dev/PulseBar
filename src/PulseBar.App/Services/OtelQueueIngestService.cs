using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Configuration;
using PulseBar.Core.Models;
using PulseBar.Storage.Sqlite;

namespace PulseBar.App.Services;

/// <summary>
/// Ingests normalized token events queued as JSONL by the WSL bridge helper
/// (spec §10.6). Ingestion is idempotent (event_key primary key), so re-reading
/// a file is safe; fully-ingested large files are truncated to keep disk bounded.
/// </summary>
public sealed class OtelQueueIngestService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private const long TruncateThresholdBytes = 5 * 1024 * 1024;

    private readonly IAppPaths _paths;
    private readonly ITokenUsageRepository _repository;
    private readonly ILogger<OtelQueueIngestService> _logger;
    private readonly Dictionary<string, long> _lastSeenLength = new(StringComparer.OrdinalIgnoreCase);

    public OtelQueueIngestService(
        IAppPaths paths,
        ITokenUsageRepository repository,
        ILogger<OtelQueueIngestService> logger)
    {
        _paths = paths;
        _repository = repository;
        _logger = logger;
    }

    public event EventHandler? EventsIngested;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueDir = Path.Combine(_paths.BridgeDir, "claude-events");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    IngestPending(queueDir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Queue ingestion pass failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void IngestPending(string queueDir)
    {
        if (!Directory.Exists(queueDir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(queueDir, "*.jsonl"))
        {
            var length = new FileInfo(file).Length;
            if (_lastSeenLength.TryGetValue(file, out var seen) && seen == length)
            {
                continue; // Unchanged since last pass.
            }

            var events = new List<TokenUsageEvent>();
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                while (reader.ReadLine() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        if (JsonSerializer.Deserialize<TokenUsageEvent>(line, JsonOptions) is { } tokenEvent)
                        {
                            events.Add(tokenEvent);
                        }
                    }
                    catch (JsonException)
                    {
                        // A torn/partial line is skipped; the next pass re-reads it.
                    }
                }
            }

            _lastSeenLength[file] = length;

            if (events.Count > 0 && _repository.UpsertEvents(events) > 0)
            {
                EventsIngested?.Invoke(this, EventArgs.Empty);
            }

            if (length > TruncateThresholdBytes)
            {
                TryTruncate(file);
            }
        }
    }

    private void TryTruncate(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Truncate, FileAccess.Write, FileShare.None);
            _lastSeenLength[file] = 0;
        }
        catch (IOException)
        {
            // The writer holds it right now; try again on a later pass.
        }
    }
}
