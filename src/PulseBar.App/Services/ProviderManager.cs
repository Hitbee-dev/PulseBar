using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Configuration;
using PulseBar.Core.Interfaces;
using PulseBar.Core.Models;
using PulseBar.Providers.Codex;
using PulseBar.Windows.Environments;

namespace PulseBar.App.Services;

/// <summary>
/// Owns all AI usage providers: detects CLIs on first run, starts providers for
/// enabled profiles, and aggregates their snapshots for the UI.
/// A provider failure never affects system metrics collection.
/// </summary>
public sealed class ProviderManager : BackgroundService
{
    private readonly IConfigurationService _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ProviderManager> _logger;
    private readonly List<IUsageProvider> _providers = [];
    private readonly ConcurrentDictionary<string, UsageSnapshot> _snapshots = new();

    public ProviderManager(
        IConfigurationService config,
        ILoggerFactory loggerFactory,
        ILogger<ProviderManager> logger)
    {
        _config = config;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public event EventHandler<IReadOnlyList<UsageSnapshot>>? SnapshotsUpdated;

    public IReadOnlyList<UsageSnapshot> CurrentSnapshots
        => _snapshots.Values.OrderBy(s => s.ProviderId, StringComparer.Ordinal).ToList();

    public void RefreshAll()
    {
        foreach (var provider in _providers)
        {
            _ = provider.RefreshAsync(CancellationToken.None);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await EnsureProfilesDetectedAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLI auto-detection failed; providers can be configured manually.");
        }

        foreach (var profileConfig in _config.Current.Providers.Where(p => p.Enabled))
        {
            try
            {
                var provider = CreateProvider(profileConfig);
                if (provider is null)
                {
                    continue;
                }

                provider.SnapshotUpdated += (_, snapshot) =>
                {
                    _snapshots[snapshot.ProviderId] = snapshot;
                    SnapshotsUpdated?.Invoke(this, CurrentSnapshots);
                };
                provider.StateChanged += (_, state) => _logger.LogInformation(
                    "Provider {Provider}: {State} {Message}",
                    state.ProviderId, state.State, state.Message ?? "");

                _providers.Add(provider);
                await provider.StartAsync(profileConfig.ToProfile(), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start provider {ProviderId}.", profileConfig.ProviderId);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            try
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider dispose failed.");
            }
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private IUsageProvider? CreateProvider(ProviderProfileConfig profile)
    {
        switch (profile.ProviderId)
        {
            case "codex":
                var options = new CodexProviderOptions
                {
                    RefreshInterval = TimeSpan.FromSeconds(Math.Max(15, profile.RefreshIntervalSeconds)),
                };
                return new CodexProvider(_loggerFactory.CreateLogger<CodexProvider>(), options);
            case "claude":
                _logger.LogInformation("Claude provider profile found; implemented in a later phase.");
                return null;
            default:
                _logger.LogWarning("Unknown provider id '{ProviderId}' in config.", profile.ProviderId);
                return null;
        }
    }

    /// <summary>First run only: detect codex CLIs and persist profiles for them.</summary>
    private async Task EnsureProfilesDetectedAsync(CancellationToken cancellationToken)
    {
        if (_config.Current.Providers.Any(p => p.ProviderId == "codex"))
        {
            return;
        }

        var detected = await CliDetector.DetectAsync("codex", cancellationToken).ConfigureAwait(false);
        if (detected.Count == 0)
        {
            _logger.LogInformation("No codex CLI detected (Windows/WSL).");
            return;
        }

        // Prefer Windows native when both exist; one profile is enough for MVP.
        var best = detected
            .OrderBy(d => d.Environment == ExecutionEnvironmentType.WindowsNative ? 0 : 1)
            .First();

        _logger.LogInformation(
            "Detected codex: {Environment} {Distro} {Path}",
            best.Environment, best.WslDistribution ?? "-", best.ExecutablePath);

        _config.Update(c => c.Providers.Add(new ProviderProfileConfig
        {
            Id = best.Environment == ExecutionEnvironmentType.Wsl ? $"codex-wsl" : "codex-windows",
            ProviderId = "codex",
            Environment = best.Environment,
            ExecutablePath = best.ExecutablePath,
            WslDistribution = best.WslDistribution,
            Enabled = true,
            RefreshIntervalSeconds = 60,
        }));
    }
}
