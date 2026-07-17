namespace PulseBar.Core.Models;

/// <summary>How trustworthy/recent a displayed value is.</summary>
public enum DataFreshness
{
    Live,
    Fresh,
    Stale,
    Unavailable,
    AuthenticationRequired,
    Error
}
