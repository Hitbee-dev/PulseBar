using PulseBar.Windows.Startup;

namespace PulseBar.Windows.Tests.Startup;

public class StartupManagerTests
{
    [Fact]
    public void BuildCommand_QuotesExecutablePath()
    {
        var command = StartupManager.BuildCommand(@"C:\Program Files\PulseBar\PulseBar.exe");

        Assert.Equal("\"C:\\Program Files\\PulseBar\\PulseBar.exe\"", command);
    }
}
