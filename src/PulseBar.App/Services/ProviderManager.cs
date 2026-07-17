using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Configuration;
using PulseBar.Core.Interfaces;
using PulseBar.Core.Models;
using PulseBar.Providers.Claude;
using PulseBar.Providers.Claude.OpenTelemetry;
using PulseBar.Providers.Claude.Statusline;
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
    private readonly IAppPaths _paths;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ProviderManager> _logger;
    private readonly List<(ProviderProfileConfig Profile, IUsageProvider Provider)> _providers = [];
    private readonly ConcurrentDictionary<string, UsageSnapshot> _snapshots = new();

    public ProviderManager(
        IConfigurationService config,
        IAppPaths paths,
        ILoggerFactory loggerFactory,
        ILogger<ProviderManager> logger)
    {
        _config = config;
        _paths = paths;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public event EventHandler<IReadOnlyList<UsageSnapshot>>? SnapshotsUpdated;

    public IReadOnlyList<UsageSnapshot> CurrentSnapshots
        => _snapshots.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();

    public void RefreshAll()
    {
        foreach (var (_, provider) in _providers)
        {
            _ = provider.RefreshAsync(CancellationToken.None);
        }
    }

    /// <summary>Starts the official Codex browser login; returns the auth URL to open.</summary>
    public async Task<string?> BeginCodexLoginAsync(CancellationToken cancellationToken)
    {
        var codex = _providers.Select(p => p.Provider).OfType<CodexProvider>().FirstOrDefault();
        if (codex is null)
        {
            return null;
        }

        return await codex.BeginLoginAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Manual statusline install. With <paramref name="wrapExisting"/> (user consent
    /// given) a foreign statusLine is wrapped so both PulseBar and the original HUD run.
    /// </summary>
    public async Task<StatuslineInstallResult?> InstallStatuslineAsync(
        bool wrapExisting,
        CancellationToken cancellationToken)
    {
        var profile = _config.Current.Providers.FirstOrDefault(p => p.ProviderId == "claude");
        if (profile is null)
        {
            _logger.LogInformation("Statusline install requested but no Claude profile is configured.");
            return null;
        }

        var bridgeExe = Path.Combine(AppContext.BaseDirectory, "PulseBar.Bridge.exe");
        if (!File.Exists(bridgeExe))
        {
            _logger.LogWarning("Bridge executable not found at {Path}; cannot install statusline.", bridgeExe);
            return null;
        }

        var settingsPath = await ResolveClaudeSettingsPathAsync(profile, cancellationToken).ConfigureAwait(false);
        if (settingsPath is null)
        {
            return StatuslineInstallResult.SettingsUnreadable;
        }

        var isWsl = profile.Environment == ExecutionEnvironmentType.Wsl;
        var result = wrapExisting
            ? ClaudeSettingsInstaller.InstallWrapped(
                settingsPath, bridgeExe, _paths.ClaudeStatusCacheFile, isWsl, _paths.BackupsDir)
            : ClaudeSettingsInstaller.Install(
                settingsPath,
                ClaudeSettingsInstaller.BuildBridgeCommand(bridgeExe, _paths.ClaudeStatusCacheFile, isWsl),
                _paths.BackupsDir);

        _logger.LogInformation("Claude statusline install (wrap={Wrap}): {Result}", wrapExisting, result);
        return result;
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

                var profileId = profileConfig.Id;
                provider.SnapshotUpdated += (_, snapshot) =>
                {
                    // Keyed by profile so multiple profiles of one provider coexist.
                    _snapshots[profileId] = snapshot;
                    SnapshotsUpdated?.Invoke(this, CurrentSnapshots);
                };
                provider.StateChanged += (_, state) => _logger.LogInformation(
                    "Provider {Provider}: {State} {Message}",
                    state.ProviderId, state.State, state.Message ?? "");

                _providers.Add((profileConfig, provider));
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
        foreach (var (_, provider) in _providers)
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
                return new ClaudeProvider(
                    _loggerFactory.CreateLogger<ClaudeProvider>(),
                    new ClaudeProviderOptions { CachePath = _paths.ClaudeStatusCacheFile });
            default:
                _logger.LogWarning("Unknown provider id '{ProviderId}' in config.", profile.ProviderId);
                return null;
        }
    }

    /// <summary>First run only: detect provider CLIs and persist profiles for them.</summary>
    private async Task EnsureProfilesDetectedAsync(CancellationToken cancellationToken)
    {
        await DetectAndAddProfileAsync("codex", cancellationToken).ConfigureAwait(false);
        var claudeProfile = await DetectAndAddProfileAsync("claude", cancellationToken).ConfigureAwait(false)
            ?? _config.Current.Providers.FirstOrDefault(p => p.ProviderId == "claude");

        if (claudeProfile is not null)
        {
            await TryInstallClaudeStatuslineAsync(claudeProfile, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ProviderProfileConfig?> DetectAndAddProfileAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        if (_config.Current.Providers.Any(p => p.ProviderId == providerId))
        {
            return null;
        }

        var detected = await CliDetector.DetectAsync(providerId, cancellationToken).ConfigureAwait(false);
        if (detected.Count == 0)
        {
            _logger.LogInformation("No {Provider} CLI detected (Windows/WSL).", providerId);
            return null;
        }

        // Prefer Windows native when both exist; one profile is enough for MVP.
        var best = detected
            .OrderBy(d => d.Environment == ExecutionEnvironmentType.WindowsNative ? 0 : 1)
            .First();

        _logger.LogInformation(
            "Detected {Provider}: {Environment} {Distro} {Path}",
            providerId, best.Environment, best.WslDistribution ?? "-", best.ExecutablePath);

        var profile = new ProviderProfileConfig
        {
            Id = best.Environment == ExecutionEnvironmentType.Wsl
                ? $"{providerId}-wsl"
                : $"{providerId}-windows",
            ProviderId = providerId,
            Environment = best.Environment,
            ExecutablePath = best.ExecutablePath,
            WslDistribution = best.WslDistribution,
            Enabled = true,
            RefreshIntervalSeconds = 60,
        };
        _config.Update(c => c.Providers.Add(profile));
        return profile;
    }

    /// <summary>
    /// Registers the bridge as Claude Code's statusLine when none is configured.
    /// An existing foreign statusLine is never touched (spec §10.2) — the merge
    /// instructions are logged instead.
    /// </summary>
    private async Task TryInstallClaudeStatuslineAsync(
        ProviderProfileConfig profile,
        CancellationToken cancellationToken)
    {
        try
        {
            var bridgeExe = Path.Combine(AppContext.BaseDirectory, "PulseBar.Bridge.exe");
            if (!File.Exists(bridgeExe))
            {
                _logger.LogWarning("Bridge executable not found at {Path}; cannot install statusline.", bridgeExe);
                return;
            }

            var isWsl = profile.Environment == ExecutionEnvironmentType.Wsl;
            var settingsPath = await ResolveClaudeSettingsPathAsync(profile, cancellationToken).ConfigureAwait(false);
            if (settingsPath is null)
            {
                _logger.LogWarning("Could not resolve Claude settings path; skipping statusline install.");
                return;
            }

            var command = ClaudeSettingsInstaller.BuildBridgeCommand(
                bridgeExe, _paths.ClaudeStatusCacheFile, isWsl);
            var result = ClaudeSettingsInstaller.Install(settingsPath, command, _paths.BackupsDir);

            switch (result)
            {
                case StatuslineInstallResult.Installed:
                    _logger.LogInformation("Claude statusline bridge installed into {Path}.", settingsPath);
                    break;
                case StatuslineInstallResult.AlreadyInstalled:
                    _logger.LogInformation("Claude statusline bridge already installed.");
                    break;
                case StatuslineInstallResult.ExistingStatusLine:
                    _logger.LogWarning(
                        "Claude settings already define a statusLine; not touching it. " +
                        "To feed PulseBar too, wrap your current command manually: " +
                        "{Command} --passthrough \"<your current statusline command>\"",
                        command);
                    break;
                case StatuslineInstallResult.SettingsUnreadable:
                    _logger.LogWarning("Claude settings.json could not be parsed; nothing was modified.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Claude statusline installation failed.");
        }
    }

    /// <summary>
    /// Claude OTel telemetry setup — runs as the second half of the user-initiated
    /// Claude connect flow (tray menu or detail popup); never invoked automatically.
    /// Returns a localization key describing the outcome.
    /// </summary>
    public async Task<string> InstallOtelEnvAsync(
        string? receiverEndpoint,
        string? receiverSecret,
        CancellationToken cancellationToken)
    {
        if (receiverEndpoint is null || receiverSecret is null)
        {
            return "Otel_ReceiverDown";
        }

        var profile = _config.Current.Providers.FirstOrDefault(p => p.ProviderId == "claude");
        if (profile is null)
        {
            return "Otel_NoProfile";
        }

        var settingsPath = await ResolveClaudeSettingsPathAsync(profile, cancellationToken).ConfigureAwait(false);
        if (settingsPath is null)
        {
            return "Otel_Unreadable";
        }

        var result = OtelEnvInstaller.Install(settingsPath, receiverEndpoint, receiverSecret, _paths.BackupsDir);
        _logger.LogInformation("Claude OTel env install: {Result} ({Path})", result, settingsPath);

        return result switch
        {
            OtelInstallResult.Installed => "Otel_Installed",
            OtelInstallResult.AlreadyInstalled => "Otel_AlreadyInstalled",
            OtelInstallResult.ConflictingEnv => "Otel_Conflict",
            _ => "Otel_Unreadable",
        };
    }

    private async Task<string?> ResolveClaudeSettingsPathAsync(
        ProviderProfileConfig profile,
        CancellationToken cancellationToken)
    {
        if (profile.Environment != ExecutionEnvironmentType.Wsl)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "settings.json");
        }

        var home = profile.LinuxHome
            ?? await CliDetector.GetWslHomeAsync(profile.WslDistribution, cancellationToken).ConfigureAwait(false);
        if (home is null)
        {
            return null;
        }

        if (profile.LinuxHome != home)
        {
            profile.LinuxHome = home;
            _config.Update(_ => { }); // Persist the resolved home.
        }

        return $@"\\wsl.localhost\{profile.WslDistribution}{home.Replace('/', '\\')}\.claude\settings.json";
    }
}
