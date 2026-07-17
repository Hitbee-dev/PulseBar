using Microsoft.Extensions.Logging;

namespace PulseBar.Core.Logging;

/// <summary>
/// Minimal daily log file provider (no NuGet dependency).
/// One file per day: pulsebar-yyyyMMdd.log. Old files are pruned on startup
/// by age and total size.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    public FileLoggerProvider(string logsDir, int retentionDays = 7, long maxTotalBytes = 50 * 1024 * 1024)
    {
        Directory.CreateDirectory(logsDir);
        Cleanup(logsDir, retentionDays, maxTotalBytes);

        var file = Path.Combine(logsDir, $"pulsebar-{DateTime.Now:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {category}: {message}");
            if (exception is not null)
            {
                _writer.WriteLine(exception.ToString());
            }
        }
    }

    public static void Cleanup(string logsDir, int retentionDays, long maxTotalBytes)
    {
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        var files = new DirectoryInfo(logsDir)
            .GetFiles("pulsebar-*.log")
            .OrderBy(f => f.LastWriteTime)
            .ToList();

        foreach (var file in files.Where(f => f.LastWriteTime < cutoff).ToList())
        {
            TryDelete(file);
            files.Remove(file);
        }

        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= maxTotalBytes)
            {
                break;
            }

            total -= file.Length;
            TryDelete(file);
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (IOException)
        {
            // A file locked by another instance is skipped; it will be pruned next run.
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
