using PulseBar.Core.Models;

namespace PulseBar.Core.Interfaces;

/// <summary>
/// Contract every AI usage provider (Claude, Codex, ...) implements.
/// Providers never reference UI types.
/// </summary>
public interface IUsageProvider : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }

    Task<ProviderCapability> ProbeAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken);

    Task StartAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken);

    Task<UsageSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken);

    Task RefreshAsync(
        CancellationToken cancellationToken);

    event EventHandler<UsageSnapshot>? SnapshotUpdated;
    event EventHandler<ProviderStateChanged>? StateChanged;
}
