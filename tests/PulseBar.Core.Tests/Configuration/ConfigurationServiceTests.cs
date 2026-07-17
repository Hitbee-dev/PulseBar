using Microsoft.Extensions.Logging.Abstractions;
using PulseBar.Core.Configuration;
using PulseBar.Core.Models;

namespace PulseBar.Core.Tests.Configuration;

public sealed class ConfigurationServiceTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly ConfigurationService _service;

    public ConfigurationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PulseBarTests", Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _service = new ConfigurationService(_paths, NullLogger<ConfigurationService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenFileMissing_UsesDefaults()
    {
        _service.Load();

        Assert.Equal(1, _service.Current.SchemaVersion);
        Assert.Equal("ko-KR", _service.Current.Appearance.Language);
        Assert.Equal(1000, _service.Current.Metrics.PollIntervalMs);
        Assert.Empty(_service.Current.Providers);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        _service.Load();
        _service.Update(c =>
        {
            c.Appearance.Language = "en-US";
            c.Providers.Add(new ProviderProfileConfig
            {
                Id = "claude-wsl",
                ProviderId = "claude",
                Environment = ExecutionEnvironmentType.Wsl,
                WslDistribution = "Ubuntu-22.04",
                Enabled = true,
            });
        });

        var reloaded = new ConfigurationService(_paths, NullLogger<ConfigurationService>.Instance);
        reloaded.Load();

        Assert.Equal("en-US", reloaded.Current.Appearance.Language);
        var provider = Assert.Single(reloaded.Current.Providers);
        Assert.Equal("claude-wsl", provider.Id);
        Assert.Equal(ExecutionEnvironmentType.Wsl, provider.Environment);
        Assert.True(provider.Enabled);
    }

    [Fact]
    public void Save_WritesCamelCaseEnumsAndProperties()
    {
        _service.Load();
        _service.Update(c => c.Providers.Add(new ProviderProfileConfig
        {
            Id = "codex-wsl",
            ProviderId = "codex",
            Environment = ExecutionEnvironmentType.Wsl,
        }));

        var json = File.ReadAllText(_paths.ConfigFile);

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"wsl\"", json);
        Assert.Contains("\"taskbarOverlay\"", json);
    }

    [Fact]
    public void Load_WhenFileCorrupt_BacksUpAndUsesDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.ConfigFile)!);
        File.WriteAllText(_paths.ConfigFile, "{ not valid json !!");

        _service.Load();

        Assert.Equal(1, _service.Current.SchemaVersion);
        var backups = Directory.GetFiles(_paths.BackupsDir, "config.corrupt.*.json");
        Assert.Single(backups);
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        _service.Load();
        _service.Save();

        Assert.True(File.Exists(_paths.ConfigFile));
        Assert.False(File.Exists(_paths.ConfigFile + ".tmp"));
    }

    [Fact]
    public void Update_RaisesConfigChanged()
    {
        _service.Load();
        AppConfig? seen = null;
        _service.ConfigChanged += (_, c) => seen = c;

        _service.Update(c => c.Appearance.Language = "en-US");

        Assert.NotNull(seen);
        Assert.Equal("en-US", seen!.Appearance.Language);
    }
}
