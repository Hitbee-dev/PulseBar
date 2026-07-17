using PulseBar.Bridge.Commands;

namespace PulseBar.Bridge;

/// <summary>
/// Lightweight bridge executable. Commands:
///   claude-statusline --output &lt;cache-path&gt; [--passthrough &lt;command&gt;]
/// Errors never propagate as non-zero exit codes: this binary sits inside the
/// Claude Code statusline pipeline and must never disturb it.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "claude-statusline")
            {
                return ClaudeStatuslineCommand.Run(args[1..], Console.In, Console.Out, Console.Error);
            }

            Console.Error.WriteLine("PulseBar.Bridge: unknown or missing command.");
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                Console.Error.WriteLine($"PulseBar.Bridge: {ex.GetType().Name}");
            }
            catch (IOException)
            {
            }

            return 0;
        }
    }
}
