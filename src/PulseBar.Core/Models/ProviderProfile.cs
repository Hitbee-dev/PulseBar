namespace PulseBar.Core.Models;

/// <summary>Where a provider CLI lives (Windows native vs. a WSL distro).</summary>
public sealed record ProviderProfile(
    string Id,
    string ProviderId,
    ExecutionEnvironmentType Environment,
    string? ExecutablePath,
    string? WslDistribution,
    string? LinuxHome,
    bool Enabled);
