namespace PulseBar.Core.Models;

public enum ProviderConnectionState
{
    Disabled,
    Detecting,
    ExecutableMissing,
    AuthenticationRequired,
    Connecting,
    Connected,
    Stale,
    Disconnected,
    Error
}
