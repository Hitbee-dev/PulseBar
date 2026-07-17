using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Models;

namespace PulseBar.Providers.Codex.AppServer;

/// <summary>
/// Owns one long-lived `codex app-server` process (Windows native or inside WSL).
/// stdout/stderr are read asynchronously as UTF-8; disposal closes stdin first
/// and kills the whole process tree if the server doesn't exit in time.
/// </summary>
public sealed class AppServerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly Task _stderrPump;

    private AppServerProcess(Process process, ILogger logger)
    {
        _process = process;
        _logger = logger;
        Writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        Reader = process.StandardOutput;
        _stderrPump = Task.Run(PumpStderrAsync);
    }

    public TextReader Reader { get; }

    public TextWriter Writer { get; }

    public bool HasExited => _process.HasExited;

    public static AppServerProcess Start(ProviderProfile profile, ILogger logger)
    {
        var startInfo = BuildStartInfo(profile);
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start codex app-server process.");
        logger.LogInformation(
            "Started codex app-server (pid {Pid}): {File} {Args}",
            process.Id, startInfo.FileName, string.Join(' ', startInfo.ArgumentList));
        return new AppServerProcess(process, logger);
    }

    /// <summary>Pure builder, unit-testable without spawning anything.</summary>
    public static ProcessStartInfo BuildStartInfo(ProviderProfile profile)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var executable = string.IsNullOrWhiteSpace(profile.ExecutablePath) ? "codex" : profile.ExecutablePath;

        if (profile.Environment == ExecutionEnvironmentType.Wsl)
        {
            startInfo.FileName = "wsl.exe";
            if (!string.IsNullOrWhiteSpace(profile.WslDistribution))
            {
                startInfo.ArgumentList.Add("-d");
                startInfo.ArgumentList.Add(profile.WslDistribution);
            }

            startInfo.ArgumentList.Add("--");
            if (BuildWslPathVariable(executable) is { } pathVariable)
            {
                // No shell string: wsl.exe mangles quoted arguments, so the PATH
                // override travels as a plain env token (nvm node-shim CLIs need
                // their own bin directory on PATH in a non-login session).
                startInfo.ArgumentList.Add("/usr/bin/env");
                startInfo.ArgumentList.Add(pathVariable);
            }

            startInfo.ArgumentList.Add(executable);
            startInfo.ArgumentList.Add("app-server");
        }
        else
        {
            startInfo.FileName = executable;
            startInfo.ArgumentList.Add("app-server");
        }

        return startInfo;
    }

    /// <summary>"PATH=&lt;exec dir&gt;:&lt;standard paths&gt;", or null for bare command names.</summary>
    public static string? BuildWslPathVariable(string executable)
    {
        var slash = executable.LastIndexOf('/');
        if (slash <= 0)
        {
            return null;
        }

        var directory = executable[..slash];
        return $"PATH={directory}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/snap/bin";
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                // Graceful: closing stdin tells the server to shut down.
                try
                {
                    Writer.Dispose();
                }
                catch (IOException)
                {
                }

                using var graceCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _process.WaitForExitAsync(graceCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while shutting down codex app-server process.");
        }
        finally
        {
            try
            {
                await _stderrPump.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            _process.Dispose();
        }
    }

    private async Task PumpStderrAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 0)
                {
                    // Never contains prompts/responses; codex logs diagnostics here.
                    _logger.LogDebug("codex app-server stderr: {Line}", Truncate(line, 500));
                }
            }
        }
        catch (Exception)
        {
            // Stream closed during shutdown.
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
