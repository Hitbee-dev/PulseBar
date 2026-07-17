using PulseBar.Core.Models;

namespace PulseBar.Core.Interfaces;

/// <summary>Produces 1-second system metric samples and keeps a short in-memory history.</summary>
public interface ISystemMetricsSource
{
    event EventHandler<SystemMetrics>? MetricsUpdated;

    SystemMetrics? Latest { get; }

    /// <summary>Most recent samples (oldest first), capped at ~5 minutes.</summary>
    IReadOnlyList<SystemMetrics> GetHistory();
}
