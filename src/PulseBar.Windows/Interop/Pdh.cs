using System.Runtime.InteropServices;

namespace PulseBar.Windows.Interop;

internal static class Pdh
{
    internal const uint ErrorSuccess = 0;
    internal const uint PdhFmtDouble = 0x00000200;
    internal const uint PdhMoreData = 0x800007D2;
    internal const uint PdhNoData = 0x800007D5;
    internal const uint PdhInvalidData = 0xC0000BC6;
    internal const uint PdhCstatusNoInstance = 0x800007D1;
    internal const uint PdhCstatusNoObject = 0xC0000BB8;
    internal const uint PdhCstatusNoCounter = 0xC0000BB9;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhOpenQueryW(string? dataSource, nuint userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhAddEnglishCounterW(
        IntPtr query, string counterPath, nuint userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Explicit)]
    internal struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll")]
    internal static extern uint PdhGetFormattedCounterValue(
        IntPtr counter, uint format, IntPtr counterType, out PDH_FMT_COUNTERVALUE value);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr Name; // LPWSTR
        public PDH_FMT_COUNTERVALUE Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr itemBuffer);
}

/// <summary>
/// Thin safe wrapper over a PDH query using English counter paths
/// (PdhAddEnglishCounterW) so it works on any Windows display language.
/// Not thread-safe; owned by the collector loop thread.
/// </summary>
public sealed class PdhQuery : IDisposable
{
    private IntPtr _query;
    private bool _disposed;

    public PdhQuery()
    {
        var status = Pdh.PdhOpenQueryW(null, 0, out _query);
        if (status != Pdh.ErrorSuccess)
        {
            throw new InvalidOperationException($"PdhOpenQuery failed: 0x{status:X8}");
        }
    }

    /// <summary>Adds a counter; returns null when the counter/object does not exist on this machine.</summary>
    public PdhCounter? TryAddCounter(string englishPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var status = Pdh.PdhAddEnglishCounterW(_query, englishPath, 0, out var counter);
        return status == Pdh.ErrorSuccess ? new PdhCounter(counter) : null;
    }

    /// <summary>Collects one sample for all counters in this query.</summary>
    public bool Collect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Pdh.PdhCollectQueryData(_query) == Pdh.ErrorSuccess;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Pdh.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _disposed = true;
        }
    }
}

public sealed class PdhCounter
{
    private readonly IntPtr _counter;

    internal PdhCounter(IntPtr counter) => _counter = counter;

    /// <summary>Formatted double value, or null when no valid data yet (first sample, vanished instance...).</summary>
    public double? ReadDouble()
    {
        var status = Pdh.PdhGetFormattedCounterValue(_counter, Pdh.PdhFmtDouble, IntPtr.Zero, out var value);
        if (status != Pdh.ErrorSuccess || value.CStatus != Pdh.ErrorSuccess)
        {
            return null;
        }

        return value.DoubleValue;
    }

    /// <summary>All wildcard instances as (name, value) pairs; empty when unavailable.</summary>
    public IReadOnlyList<(string Instance, double Value)> ReadArray()
    {
        uint bufferSize = 0;
        uint itemCount = 0;
        var status = Pdh.PdhGetFormattedCounterArrayW(
            _counter, Pdh.PdhFmtDouble, ref bufferSize, ref itemCount, IntPtr.Zero);
        if (status != Pdh.PdhMoreData || bufferSize == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            status = Pdh.PdhGetFormattedCounterArrayW(
                _counter, Pdh.PdhFmtDouble, ref bufferSize, ref itemCount, buffer);
            if (status != Pdh.ErrorSuccess)
            {
                return [];
            }

            var results = new List<(string, double)>((int)itemCount);
            var itemSize = Marshal.SizeOf<Pdh.PDH_FMT_COUNTERVALUE_ITEM>();
            for (var i = 0; i < itemCount; i++)
            {
                var item = Marshal.PtrToStructure<Pdh.PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                if (item.Value.CStatus != Pdh.ErrorSuccess)
                {
                    continue;
                }

                var name = Marshal.PtrToStringUni(item.Name) ?? "";
                results.Add((name, item.Value.DoubleValue));
            }

            return results;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
