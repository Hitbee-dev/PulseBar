using System.Diagnostics;
using System.Text;
using PulseBar.Core.Models;

namespace PulseBar.Windows.Environments;

public sealed record DetectedCli(
    ExecutionEnvironmentType Environment,
    string? WslDistribution,
    string ExecutablePath);

/// <summary>
/// Finds provider CLIs on Windows (where.exe) and inside WSL distributions
/// (wsl.exe -d ... sh -lc "command -v ..."). Only used during initial setup,
/// never on the refresh path.
/// </summary>
public static class CliDetector
{
    private static readonly string[] IgnoredDistroPrefixes =
    [
        "docker-desktop",
        "podman",
        "rancher-desktop",
    ];

    public static async Task<IReadOnlyList<DetectedCli>> DetectAsync(
        string command,
        CancellationToken cancellationToken)
    {
        var results = new List<DetectedCli>();

        var windowsPath = await RunCaptureAsync(
            "where.exe", [command], outputEncoding: null, cancellationToken).ConfigureAwait(false);
        var firstWindowsPath = windowsPath?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(firstWindowsPath))
        {
            results.Add(new DetectedCli(ExecutionEnvironmentType.WindowsNative, null, firstWindowsPath));
        }

        foreach (var distro in await ListWslDistributionsAsync(cancellationToken).ConfigureAwait(false))
        {
            var best = await FindBestInWslAsync(distro, command, cancellationToken).ConfigureAwait(false);
            if (best is not null)
            {
                results.Add(new DetectedCli(ExecutionEnvironmentType.Wsl, distro, best));
            }
        }

        return results;
    }

    /// <summary>
    /// Multiple installs of one CLI commonly coexist in a distro (snap, nvm, ~/.local/bin)
    /// and old builds break against current server APIs — probe every candidate's
    /// --version and pick the newest, not just whatever PATH finds first.
    /// </summary>
    public static async Task<string?> FindBestInWslAsync(
        string? distribution,
        string command,
        CancellationToken cancellationToken)
    {
        // `command` is an internal constant ("codex"/"claude"), never user input.
        // Each candidate runs with its own directory prepended to PATH so node-shim
        // CLIs (nvm installs) can find their interpreter in a non-login session.
        // The script travels via stdin: wsl.exe mangles quoted arguments.
        var script =
            $"for p in \"$(command -v {command})\" \"$HOME\"/.nvm/versions/node/*/bin/{command} " +
            $"\"$HOME/.local/bin/{command}\" /usr/local/bin/{command} /snap/bin/{command}; do " +
            "if [ -x \"$p\" ]; then " +
            "printf '%s|%s\\n' \"$p\" \"$(PATH=\"$(dirname \"$p\"):$PATH\" \"$p\" --version 2>/dev/null | head -1)\"; " +
            "fi; done\n";

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(distribution))
        {
            args.AddRange(["-d", distribution]);
        }

        args.AddRange(["--", "sh", "-l"]);
        var output = await RunCaptureAsync(
            "wsl.exe", args, outputEncoding: null, cancellationToken, stdinInput: script)
            .ConfigureAwait(false);
        return output is null ? null : PickBestCandidate(output.Split('\n'));
    }

    /// <summary>Picks the highest-versioned "path|version text" line (pure, unit-tested).</summary>
    public static string? PickBestCandidate(IEnumerable<string> lines)
    {
        string? bestPath = null;
        Version bestVersion = new(0, 0, 0);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var separator = line.IndexOf('|');
            if (separator <= 0)
            {
                continue;
            }

            var path = line[..separator].Trim();
            if (path.Length == 0 || !path.StartsWith('/') || !seen.Add(path))
            {
                continue;
            }

            var match = System.Text.RegularExpressions.Regex.Match(
                line[(separator + 1)..], @"(\d+)\.(\d+)\.(\d+)");
            var version = match.Success ? Version.Parse(match.Value) : new Version(0, 0, 0);

            if (bestPath is null || version > bestVersion)
            {
                bestPath = path;
                bestVersion = version;
            }
        }

        return bestPath;
    }

    /// <summary>Home directory (e.g. "/home/chan") of the default user in a distro.</summary>
    public static async Task<string?> GetWslHomeAsync(
        string? distribution,
        CancellationToken cancellationToken)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(distribution))
        {
            args.AddRange(["-d", distribution]);
        }

        args.AddRange(["--", "sh", "-lc", "echo \"$HOME\""]);
        var output = await RunCaptureAsync("wsl.exe", args, outputEncoding: null, cancellationToken)
            .ConfigureAwait(false);
        var home = output?.Trim();
        return string.IsNullOrWhiteSpace(home) || !home.StartsWith('/') ? null : home;
    }

    public static async Task<IReadOnlyList<string>> ListWslDistributionsAsync(
        CancellationToken cancellationToken)
    {
        // wsl.exe writes UTF-16LE to redirected pipes.
        var output = await RunCaptureAsync(
            "wsl.exe", ["--list", "--quiet"], Encoding.Unicode, cancellationToken).ConfigureAwait(false);
        if (output is null)
        {
            return [];
        }

        return output
            .Split('\n')
            .Select(line => line.Trim().Trim('\0', '\r'))
            .Where(line => line.Length > 0)
            .Where(line => !IgnoredDistroPrefixes.Any(
                prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static async Task<string?> RunCaptureAsync(
        string fileName,
        IReadOnlyList<string> args,
        Encoding? outputEncoding,
        CancellationToken cancellationToken,
        string? stdinInput = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdinInput is not null,
                StandardOutputEncoding = outputEncoding,
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

            if (stdinInput is not null)
            {
                await process.StandardInput.WriteAsync(stdinInput).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            return null; // Missing wsl.exe / where.exe means "not found", not a crash.
        }
    }
}
