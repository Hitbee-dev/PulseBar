namespace PulseBar.Core.Models;

/// <summary>Where a value comes from — server account quota vs. this machine only.</summary>
public enum DataScope
{
    ServerAccount,
    LocalMachine,
    LocalSession,
    Estimated
}
