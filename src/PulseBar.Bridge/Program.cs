namespace PulseBar.Bridge;

/// <summary>
/// Lightweight bridge executable. Subcommands:
///   claude-statusline --output &lt;cache-path&gt;
/// Reads Claude Code statusline JSON from stdin and writes a normalized cache file.
/// Implemented in Phase 5; skeleton only for now.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("PulseBar.Bridge: no command specified.");
            return 0;
        }

        // Never break the caller (e.g. Claude Code statusline): default to exit code 0.
        return 0;
    }
}
