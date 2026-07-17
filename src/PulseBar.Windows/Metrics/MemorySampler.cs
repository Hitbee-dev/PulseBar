using PulseBar.Windows.Interop;

namespace PulseBar.Windows.Metrics;

public sealed record MemorySample(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes - AvailableBytes;

    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * UsedBytes / TotalBytes;
}

public static class MemorySampler
{
    public static MemorySample? Sample()
    {
        var status = NativeMethods.MEMORYSTATUSEX.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            return null;
        }

        return new MemorySample(status.TotalPhys, status.AvailPhys);
    }
}
