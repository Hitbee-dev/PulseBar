using PulseBar.Core.Services;

namespace PulseBar.Core.Tests.Services;

public class RingBufferTests
{
    [Fact]
    public void Add_BelowCapacity_KeepsInsertionOrder()
    {
        var buffer = new RingBuffer<int>(5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        Assert.Equal([1, 2, 3], buffer.ToArray());
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void Add_BeyondCapacity_OverwritesOldest()
    {
        var buffer = new RingBuffer<int>(3);
        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        Assert.Equal([3, 4, 5], buffer.ToArray());
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
    }
}
