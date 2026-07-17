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
            var path = await RunCaptureAsync(
                "wsl.exe",
                ["-d", distro, "--", "sh", "-lc", $"command -v {command}"],
                outputEncoding: null,
                cancellationToken).ConfigureAwait(false);
            var trimmed = path?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                results.Add(new DetectedCli(ExecutionEnvironmentType.Wsl, distro, trimmed));
            }
        }

        return results;
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
        CancellationToken cancellationToken)
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
