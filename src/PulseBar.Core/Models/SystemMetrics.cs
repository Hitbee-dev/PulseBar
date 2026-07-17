namespace PulseBar.Core.Models;

/// <summary>
/// One 1-second sample of system resources. Any value that could not be
/// collected is null — the UI shows it as unavailable instead of 0.
/// </summary>
public sealed record SystemMetrics(
    double? CpuPercent,
    double? MemoryUsedPercent,
    ulong MemoryTotalBytes,
    ulong MemoryUsedBytes,
    double? GpuPercent,
    string? GpuAdapterName,
    ulong? VramUsedBytes,
    ulong? VramTotalBytes,
    double? DiskActivePercent,
    double? DiskReadBytesPerSec,
    double? DiskWriteBytesPerSec,
    double? NetworkReceivedBytesPerSec,
    double? NetworkSentBytesPerSec,
    DateTimeOffset CollectedAt);
