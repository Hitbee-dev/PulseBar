using System.Diagnostics;
using PulseBar.Providers.Claude.Statusline;

namespace PulseBar.Bridge.Commands;

/// <summary>
/// `PulseBar.Bridge claude-statusline --output <cache> [--passthrough <command>]`
///
/// Reads the Claude Code statusline JSON from stdin, writes the normalized quota
/// cache atomically, and (optionally) forwards the same JSON to a pre-existing
/// statusline command, echoing its stdout back to Claude Code.
///
/// Contract: never break Claude Code — always exit 0, never log prompt/response
/// content, keep total added latency minimal.
/// </summary>
public static class ClaudeStatuslineCommand
{
    private const string RecursionGuardVariable = "PULSEBAR_STATUSLINE_WRAPPED";
    private static readonly TimeSpan PassthroughTimeout = TimeSpan.FromSeconds(2);

    public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        string? outputPath = null;
        string? passthrough = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--output":
                    outputPath = args[++i];
                    break;
                case "--passthrough":
                    passthrough = args[++i];
                    break;
            }
        }

        var input = stdin.ReadToEnd();

        try
        {
            if (outputPath is not null)
            {
                var cache = StatuslineParser.Parse(input, DateTimeOffset.UtcNow);
                if (cache is not null)
                {
                    WriteAtomically(outputPath, cache.ToJson());
                }
            }
        }
        catch (Exception ex)
        {
            SafeWriteError(stderr, $"pulsebar-bridge: cache write failed ({ex.GetType().Name})");
        }

        if (!string.IsNullOrWhiteSpace(passthrough)
            && Environment.GetEnvironmentVariable(RecursionGuardVariable) != "1")
        {
            try
            {
                RunPassthrough(passthrough, input, stdout, stderr);
            }
            catch (Exception ex)
            {
                SafeWriteError(stderr, $"pulsebar-bridge: passthrough failed ({ex.GetType().Name})");
            }
        }

        return 0;
    }

    private static void RunPassthrough(string command, string input, TextWriter stdout, TextWriter stderr)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo { FileName = "cmd.exe", ArgumentList = { "/c", command } }
            : new ProcessStartInfo { FileName = "/bin/sh", ArgumentList = { "-c", command } };

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.EnvironmentVariables[RecursionGuardVariable] = "1";

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return;
        }

        process.StandardInput.Write(input);
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit((int)PassthroughTimeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            SafeWriteError(stderr, "pulsebar-bridge: passthrough statusline timed out");
            return;
        }

        stopwatch.Stop();
        if (stopwatch.Elapsed > TimeSpan.FromMilliseconds(500))
        {
            SafeWriteError(
                stderr,
                $"pulsebar-bridge: passthrough statusline took {stopwatch.ElapsedMilliseconds} ms");
        }

        stdout.Write(outputTask.GetAwaiter().GetResult());
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private static void SafeWriteError(TextWriter stderr, string message)
    {
        try
        {
            stderr.WriteLine(message);
        }
        catch (IOException)
        {
        }
    }
}
