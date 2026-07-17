using Microsoft.Extensions.Logging;
using PulseBar.Core.Logging;

namespace PulseBar.Core.Tests.Logging;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _dir;

    public FileLoggerProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PulseBarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Log_WritesToDailyFile()
    {
        using (var provider = new FileLoggerProvider(_dir))
        {
            var logger = provider.CreateLogger("Test.Category");
            logger.LogInformation("hello {Name}", "world");
        }

        var file = Path.Combine(_dir, $"pulsebar-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(file));
        var content = File.ReadAllText(file);
        Assert.Contains("hello world", content);
        Assert.Contains("Test.Category", content);
    }

    [Fact]
    public void Log_DebugLevel_IsFilteredOut()
    {
        using (var provider = new FileLoggerProvider(_dir))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogDebug("secret debug detail");
        }

        var file = Path.Combine(_dir, $"pulsebar-{DateTime.Now:yyyyMMdd}.log");
        Assert.DoesNotContain("secret debug detail", File.ReadAllText(file));
    }

    [Fact]
    public void Cleanup_RemovesFilesOlderThanRetention()
    {
        var old = Path.Combine(_dir, "pulsebar-20200101.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
        var fresh = Path.Combine(_dir, $"pulsebar-{DateTime.Now:yyyyMMdd}.log");
        File.WriteAllText(fresh, "fresh");

        FileLoggerProvider.Cleanup(_dir, retentionDays: 7, maxTotalBytes: long.MaxValue);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void Cleanup_EnforcesTotalSizeCap_DeletingOldestFirst()
    {
        var oldest = Path.Combine(_dir, "pulsebar-20260101.log");
        File.WriteAllText(oldest, new string('a', 600));
        File.SetLastWriteTime(oldest, DateTime.Now.AddDays(-2));
        var newest = Path.Combine(_dir, "pulsebar-20260102.log");
        File.WriteAllText(newest, new string('b', 600));

        FileLoggerProvider.Cleanup(_dir, retentionDays: 365, maxTotalBytes: 1000);

        Assert.False(File.Exists(oldest));
        Assert.True(File.Exists(newest));
    }
}
