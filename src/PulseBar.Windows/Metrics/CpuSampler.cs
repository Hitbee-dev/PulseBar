using PulseBar.Windows.Interop;

namespace PulseBar.Windows.Metrics;

/// <summary>
/// Total CPU usage from GetSystemTimes deltas.
/// The first sample after start (or after Reset) yields null.
/// </summary>
public sealed class CpuSampler
{
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasPrevious;

    public double? Sample()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        var curIdle = idle.ToUInt64();
        var curKernel = kernel.ToUInt64();
        var curUser = user.ToUInt64();

        double? result = null;
        if (_hasPrevious)
        {
            result = Compute(
                _prevIdle, _prevKernel, _prevUser,
                curIdle, curKernel, curUser);
        }

        _prevIdle = curIdle;
        _prevKernel = curKernel;
        _prevUser = curUser;
        _hasPrevious = true;
        return result;
    }

    /// <summary>Drops the baseline, e.g. after system sleep/resume.</summary>
    public void Reset() => _hasPrevious = false;

    /// <summary>Pure delta computation. Kernel time includes idle time.</summary>
    public static double? Compute(
        ulong prevIdle, ulong prevKernel, ulong prevUser,
        ulong curIdle, ulong curKernel, ulong curUser)
    {
        if (curIdle < prevIdle || curKernel < prevKernel || curUser < prevUser)
        {
            return null;
        }

        var idleDelta = curIdle - prevIdle;
        var totalDelta = (curKernel - prevKernel) + (curUser - prevUser);
        if (totalDelta == 0)
        {
            return null;
        }

        // Compute in double: idleDelta can slightly exceed totalDelta, and
        // unsigned subtraction would underflow to a huge value.
        var busyPercent = 100.0 * ((double)totalDelta - idleDelta) / totalDelta;
        return Math.Clamp(busyPercent, 0.0, 100.0);
    }
}
