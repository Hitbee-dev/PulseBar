using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Interfaces;
using PulseBar.Core.Models;
using PulseBar.Providers.Codex.AppServer;

namespace PulseBar.Providers.Codex;

public sealed class CodexProviderOptions
{
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan RpcTimeout { get; init; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Codex usage provider over the official `codex app-server` JSON-RPC interface.
/// Keeps one app-server process alive while enabled; reconnects with
/// 1 → 2 → 5 minute backoff. Never touches ~/.codex credentials.
/// </summary>
public sealed class CodexProvider : IUsageProvider
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
    ];

    private readonly ILogger<CodexProvider> _logger;
    private readonly CodexProviderOptions _options;
    private readonly SemaphoreSlim _refreshSignal = new(0, int.MaxValue);

    private ProviderProfile? _profile;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private UsageSnapshot? _latest;
    private string? _accountKey;
    private volatile JsonRpcClient? _activeRpc;

    public CodexProvider(ILogger<CodexProvider> logger, CodexProviderOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new CodexProviderOptions();
    }

    public string Id => "codex";

    public string DisplayName => "Codex";

    public event EventHandler<UsageSnapshot>? SnapshotUpdated;

    public event EventHandler<ProviderStateChanged>? StateChanged;

    public async Task<ProviderCapability> ProbeAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            if (profile.Environment == ExecutionEnvironmentType.Wsl)
            {
                var path = await RunCaptureAsync(
                    "wsl.exe",
                    BuildWslProbeArgs(profile.WslDistribution, profile.ExecutablePath ?? "codex"),
                    cancellationToken).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(path)
                    ? new ProviderCapability(false, null, null, "codex not found in WSL distribution")
                    : new ProviderCapability(true, path.Trim(), null, null);
            }

            if (!string.IsNullOrWhiteSpace(profile.ExecutablePath))
            {
                return File.Exists(profile.ExecutablePath)
                    ? new ProviderCapability(true, profile.ExecutablePath, null, null)
                    : new ProviderCapability(false, null, null, "configured executable path does not exist");
            }

            var found = await RunCaptureAsync("where.exe", ["codex"], cancellationToken).ConfigureAwait(false);
            var first = found?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrWhiteSpace(first)
                ? new ProviderCapability(false, null, null, "codex not found on PATH")
                : new ProviderCapability(true, first, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Codex probe failed.");
            return new ProviderCapability(false, null, null, ex.Message);
        }
    }

    public Task StartAsync(ProviderProfile profile, CancellationToken cancellationToken)
    {
        _profile = profile;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task<UsageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        => Task.FromResult(_latest ?? EmptySnapshot(DataFreshness.Unavailable));

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        _refreshSignal.Release();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the official app-server ChatGPT browser login flow
    /// (account/login/start, verified against codex-cli 0.144.4 schema).
    /// Returns the URL the user must open, or null when no connection is up.
    /// Completion arrives via the account/login/completed notification, which
    /// triggers an immediate refresh.
    /// </summary>
    public async Task<string?> BeginLoginAsync(CancellationToken cancellationToken)
    {
        var rpc = _activeRpc;
        if (rpc is null)
        {
            return null;
        }

        try
        {
            var result = await rpc.InvokeAsync(
                "account/login/start",
                new { type = "chatgpt" },
                _options.RpcTimeout,
                cancellationToken).ConfigureAwait(false);

            return result.TryGetProperty("authUrl", out var authUrl) ? authUrl.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Codex login start failed.");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            if (_runTask is not null)
            {
                try
                {
                    await _runTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            _cts.Dispose();
            _cts = null;
        }

        _refreshSignal.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ChangeState(ProviderConnectionState.Connecting, null);
                await ConnectAndPollAsync(cancellationToken).ConfigureAwait(false);
                attempt = 0; // Clean disconnect: retry quickly.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Codex connection failed.");
                ChangeState(ProviderConnectionState.Error, ex.Message);
                MarkLatestStale();
            }

            var delay = Backoff[Math.Min(attempt, Backoff.Length - 1)];
            attempt++;
            ChangeState(ProviderConnectionState.Disconnected, $"retrying in {delay.TotalMinutes:0} min");

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectAndPollAsync(CancellationToken cancellationToken)
    {
        var profile = _profile ?? throw new InvalidOperationException("StartAsync was not called.");

        await using var process = AppServerProcess.Start(profile, _logger);
        await using var rpc = new JsonRpcClient(process.Reader, process.Writer, _logger);

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rpc.Disconnected += (_, _) => disconnected.TrySetResult();
        rpc.NotificationReceived += (_, notification) =>
        {
            // Server-pushed account/limit changes (including login completion):
            // fetch a fresh snapshot right away.
            if (notification.Method is "account/rateLimits/updated" or "account/updated"
                or "account/login/completed")
            {
                if (notification.Method == "account/login/completed")
                {
                    _logger.LogInformation("Codex login flow completed.");
                }

                _refreshSignal.Release();
            }
        };

        await rpc.InvokeAsync(
            "initialize",
            new { clientInfo = new { name = "pulsebar", title = "PulseBar", version = "0.1.0" } },
            _options.RpcTimeout,
            cancellationToken).ConfigureAwait(false);
        await rpc.NotifyAsync("initialized", null, cancellationToken).ConfigureAwait(false);

        _activeRpc = rpc;
        try
        {
            while (!cancellationToken.IsCancellationRequested && !disconnected.Task.IsCompleted)
            {
                await PollOnceAsync(rpc, cancellationToken).ConfigureAwait(false);

                var wait = Task.Delay(_options.RefreshInterval, cancellationToken);
                var refresh = _refreshSignal.WaitAsync(cancellationToken);
                await Task.WhenAny(wait, refresh, disconnected.Task).ConfigureAwait(false);
            }
        }
        finally
        {
            _activeRpc = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new IOException("codex app-server exited.");
    }

    private async Task PollOnceAsync(JsonRpcClient rpc, CancellationToken cancellationToken)
    {
        var accountResult = await rpc.InvokeAsync("account/read", null, _options.RpcTimeout, cancellationToken)
            .ConfigureAwait(false);
        var account = CodexPayloadParser.ParseAccount(accountResult);

        if (!account.IsLoggedIn)
        {
            _accountKey = null;
            _latest = EmptySnapshot(DataFreshness.AuthenticationRequired);
            SnapshotUpdated?.Invoke(this, _latest);
            ChangeState(ProviderConnectionState.AuthenticationRequired, null);
            return;
        }

        // Account switch: the previous snapshot belongs to someone else — drop it.
        if (_accountKey is not null && _accountKey != account.Email)
        {
            _logger.LogInformation("Codex account changed; discarding previous snapshot.");
            _latest = null;
        }

        _accountKey = account.Email;

        // A rateLimits failure (e.g. an outdated codex binary that cannot decode a
        // new server plan type) must not kill the connection loop — keep the account
        // visible and surface the error instead.
        CodexRateLimits? rateLimits = null;
        string? rateLimitError = null;
        try
        {
            var rateLimitsResult = await rpc.InvokeAsync(
                "account/rateLimits/read", null, _options.RpcTimeout, cancellationToken).ConfigureAwait(false);
            rateLimits = CodexPayloadParser.ParseRateLimits(rateLimitsResult);
            foreach (var anomaly in rateLimits.Anomalies)
            {
                _logger.LogWarning("Codex rate-limit anomaly: {Anomaly}", anomaly);
            }
        }
        catch (JsonRpcException ex)
        {
            rateLimitError = Truncate(ex.Message, 160);
            _logger.LogWarning(
                "codex rateLimits/read failed (often an outdated codex CLI): {Message}", rateLimitError);
        }

        CodexUsage? usage = null;
        try
        {
            var usageResult = await rpc.InvokeAsync(
                "account/usage/read", null, _options.RpcTimeout, cancellationToken).ConfigureAwait(false);
            usage = CodexPayloadParser.ParseUsage(usageResult, DateOnly.FromDateTime(DateTime.Now));
        }
        catch (JsonRpcException ex)
        {
            // Token activity is optional decoration; quota data alone is still a valid snapshot.
            _logger.LogDebug("codex usage/read unavailable: {Message}", ex.Message);
        }

        var modelUsageToday = new Dictionary<string, TokenUsage>();
        var modelUsageSevenDays = new Dictionary<string, TokenUsage>();
        if (usage is not null)
        {
            modelUsageToday["codex"] = new TokenUsage(0, 0, 0, 0, usage.TodayTokens);
            modelUsageSevenDays["codex"] = new TokenUsage(0, 0, 0, 0, usage.SevenDayTokens);
        }

        _latest = new UsageSnapshot(
            ProviderId: Id,
            AccountLabel: account.Email,
            Plan: account.PlanType,
            Windows: rateLimits?.Windows ?? [],
            ModelUsageToday: modelUsageToday,
            ModelUsageSevenDays: modelUsageSevenDays,
            CreditBalance: rateLimits?.CreditBalance,
            CollectedAt: DateTimeOffset.Now,
            Freshness: rateLimits is null ? DataFreshness.Error : DataFreshness.Fresh,
            ErrorCode: rateLimits is null ? "rate-limits-unavailable" : null,
            ErrorMessage: rateLimitError);

        SnapshotUpdated?.Invoke(this, _latest);
        if (rateLimits is null)
        {
            ChangeState(ProviderConnectionState.Error, rateLimitError);
        }
        else
        {
            ChangeState(ProviderConnectionState.Connected, null);
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    private void MarkLatestStale()
    {
        if (_latest is { Freshness: DataFreshness.Fresh or DataFreshness.Live } snapshot)
        {
            _latest = snapshot with
            {
                Freshness = DataFreshness.Stale,
                Windows = snapshot.Windows.Select(w => w with { Freshness = DataFreshness.Stale }).ToList(),
            };
            SnapshotUpdated?.Invoke(this, _latest);
        }
    }

    private UsageSnapshot EmptySnapshot(DataFreshness freshness)
        => new(
            Id, null, null,
            [],
            new Dictionary<string, TokenUsage>(),
            new Dictionary<string, TokenUsage>(),
            null,
            DateTimeOffset.Now,
            freshness,
            null,
            null);

    private void ChangeState(ProviderConnectionState state, string? message)
        => StateChanged?.Invoke(this, new ProviderStateChanged(Id, state, message));

    private static IReadOnlyList<string> BuildWslProbeArgs(string? distribution, string command)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(distribution))
        {
            args.Add("-d");
            args.Add(distribution);
        }

        args.Add("--");
        args.Add("sh");
        args.Add("-lc");
        args.Add($"command -v {command}");
        return args;
    }

    private static async Task<string?> RunCaptureAsync(
        string fileName,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0 ? output : null;
    }
}
