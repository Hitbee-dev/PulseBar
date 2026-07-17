using System.Text.RegularExpressions;

namespace PulseBar.Core.Configuration;

/// <summary>
/// Sanity checks applied to imported config files (settings import is the one
/// path where provider profile fields can arrive from outside this machine).
/// </summary>
public static partial class AppConfigValidator
{
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex WslDistributionPattern();

    private static readonly char[] ForbiddenExecutableChars = ['&', '|', '<', '>', '^', '"', '\r', '\n'];

    public static bool IsValid(AppConfig config)
    {
        if (config.SchemaVersion < 1)
        {
            return false;
        }

        foreach (var profile in config.Providers)
        {
            if (profile.WslDistribution is { } distro
                && (distro.Length == 0 || !WslDistributionPattern().IsMatch(distro)))
            {
                return false;
            }

            if (profile.ExecutablePath is { } path
                && (path.Length == 0 || path.IndexOfAny(ForbiddenExecutableChars) >= 0))
            {
                return false;
            }
        }

        return true;
    }
}
