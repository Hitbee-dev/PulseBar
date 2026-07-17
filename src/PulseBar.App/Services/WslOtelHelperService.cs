using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Configuration;
using PulseBar.Core.Models;
using PulseBar.Providers.Claude.Statusline;
using PulseBar.Windows.Security;

namespace PulseBar.App.Services;

/// <summary>
/// Keeps the linux bridge helper (`PulseBar.Bridge otel-receiver`) running inside
/// the user's WSL distro so Claude Code in WSL can export OTLP to WSL-loopback.
/// The helper appends normalized events to a /mnt/c JSONL queue which
/// <see cref="OtelQueueIngestService"/> drains. The bearer secret travels via
/// the helper's stdin (never on its command line). Restarts with backoff.
/// </summary>
public sealed class WslOtelHelperService : BackgroundService
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
    ];

    private readonly IConfigurationService _config;
    private readonly IAppPaths _paths;
    private readonly ILogger<WslOtelHelperService> _logger;

    public WslOtelHelperService(
        IConfigurationService config,
        IAppPaths paths,
        ILogger<WslOtelHelperService> logger)
    {
        _config = config;
        _paths = paths;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give ProviderManager time to finish first-run detection.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var profile = _config.Current.Providers.FirstOrDefault(
            p => p.ProviderId == "claude" && p.Environment == ExecutionEnvironmentType.Wsl && p.Enabled);
        if (profile is null)
        {
            return;
        }

        var helperPath = Path.Combine(AppContext.BaseDirectory, "wsl-bridge", "PulseBar.Bridge");
        if (!File.Exists(helperPath))
        {
            _logger.LogInformation(
                "WSL OTel helper binary not found at {Path}; WSL Fable telemetry inactive " +
                "(portable/installer builds include it).", helperPath);
            return;
        }

        var secret = OtelSecretStore.GetOrCreate(Path.Combine(_paths.BridgeDir, "otel.secret"));
        var queueWinPath = Path.Combine(_paths.BridgeDir, "claude-events", "wsl-queue.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(queueWinPath)!);

        var helperWslPath = ClaudeSettingsInstaller.ToWslPath(helperPath);
        var queueWslPath = ClaudeSettingsInstaller.ToWslPath(queueWinPath);

        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunHelperOnceAsync(profile, helperWslPath, queueWslPath, secret, stoppingToken)
                    .ConfigureAwait(false);
                attempt = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WSL OTel helper failed.");
            }

            var delay = Backoff[Math.Min(attempt, Backoff.Length - 1)];
            attempt++;
            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunHelperOnceAsync(
        ProviderProfileConfig profile,
        string helperWslPath,
        string queueWslPath,
        string secret,
        CancellationToken stoppingToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!string.IsNullOrWhiteSpace(profile.WslDistribution))
        {
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(profile.WslDistribution);
        }

        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(helperWslPath);
        startInfo.ArgumentList.Add("otel-receiver");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("127.0.0.1:4318");
        startInfo.ArgumentList.Add("--queue");
        startInfo.ArgumentList.Add(queueWslPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start WSL OTel helper.");

        _logger.LogInformation("WSL OTel helper started (pid {Pid}).", process.Id);

        await process.StandardInput.WriteLineAsync(secret).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(stoppingToken).ConfigureAwait(false);

        var stderrPump = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 0)
                {
                    // Low volume (startup/shutdown diagnostics only) — keep visible.
                    _logger.LogInformation("wsl otel helper: {Line}", line);
                }
            }
        }, CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("WSL OTel helper exited with code {Code}.", process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }
        finally
        {
            await stderrPump.ConfigureAwait(false);
        }
    }
}
