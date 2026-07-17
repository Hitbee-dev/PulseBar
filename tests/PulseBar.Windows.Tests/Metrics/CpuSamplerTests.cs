using PulseBar.Windows.Metrics;

namespace PulseBar.Windows.Tests.Metrics;

public class CpuSamplerTests
{
    [Fact]
    public void Compute_TypicalDeltas_ReturnsBusyPercent()
    {
        // kernel includes idle: total = kernel + user = 800 + 200 = 1000, idle = 500 → busy 50%
        var result = CpuSampler.Compute(
            prevIdle: 0, prevKernel: 0, prevUser: 0,
            curIdle: 500, curKernel: 800, curUser: 200);

        Assert.NotNull(result);
        Assert.Equal(50.0, result!.Value, precision: 5);
    }

    [Fact]
    public void Compute_ZeroTotalDelta_ReturnsNull()
    {
        var result = CpuSampler.Compute(10, 20, 30, 10, 20, 30);

        Assert.Null(result);
    }

    [Fact]
    public void Compute_CounterWentBackwards_ReturnsNull()
    {
        var result = CpuSampler.Compute(
            prevIdle: 1000, prevKernel: 2000, prevUser: 3000,
            curIdle: 900, curKernel: 2100, curUser: 3100);

        Assert.Null(result);
    }

    [Fact]
    public void Compute_IdleExceedsTotal_ClampsToZero()
    {
        // Rounding in the kernel can make idle delta slightly exceed total delta.
        var result = CpuSampler.Compute(0, 0, 0, curIdle: 1100, curKernel: 1000, curUser: 0);

        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Sample_FirstCall_ReturnsNull_SecondCallReturnsValue()
    {
        var sampler = new CpuSampler();

        var first = sampler.Sample();
        Thread.Sleep(50);
        var second = sampler.Sample();

        Assert.Null(first);
        Assert.NotNull(second);
        Assert.InRange(second!.Value, 0.0, 100.0);
    }

    [Fact]
    public void Reset_DropsBaseline_NextSampleIsNull()
    {
        var sampler = new CpuSampler();
        sampler.Sample();
        sampler.Reset();

        Assert.Null(sampler.Sample());
    }
}
