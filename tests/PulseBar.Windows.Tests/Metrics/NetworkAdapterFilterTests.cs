using PulseBar.Windows.Metrics;

namespace PulseBar.Windows.Tests.Metrics;

public class NetworkAdapterFilterTests
{
    [Fact]
    public void Sum_AddsAllRealAdapters()
    {
        var values = new (string, double)[]
        {
            ("Intel[R] Ethernet Connection", 100),
            ("Realtek Wi-Fi 6 Adapter", 50),
        };

        Assert.Equal(150, NetworkAdapterFilter.Sum(values, [], []));
    }

    [Fact]
    public void Sum_ExcludesLoopbackAlways()
    {
        var values = new (string, double)[]
        {
            ("Loopback Pseudo-Interface 1", 999),
            ("Intel[R] Ethernet Connection", 100),
        };

        Assert.Equal(100, NetworkAdapterFilter.Sum(values, [], []));
    }

    [Fact]
    public void Sum_UserExclusions_AreRemoved()
    {
        var values = new (string, double)[]
        {
            ("vEthernet [WSL]", 500),
            ("Intel[R] Ethernet Connection", 100),
        };

        Assert.Equal(100, NetworkAdapterFilter.Sum(values, [], ["vEthernet"]));
    }

    [Fact]
    public void Sum_IncludeList_OnlyCountsMatches()
    {
        var values = new (string, double)[]
        {
            ("vEthernet [WSL]", 500),
            ("Intel[R] Ethernet Connection", 100),
            ("Tailscale Tunnel", 30),
        };

        Assert.Equal(100, NetworkAdapterFilter.Sum(values, ["Intel"], []));
    }

    [Fact]
    public void Sum_NegativeValues_AreIgnored()
    {
        var values = new (string, double)[] { ("Intel Ethernet", -5) };

        Assert.Equal(0, NetworkAdapterFilter.Sum(values, [], []));
    }
}
