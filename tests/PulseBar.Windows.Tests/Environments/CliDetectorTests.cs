using PulseBar.Windows.Environments;

namespace PulseBar.Windows.Tests.Environments;

public class CliDetectorTests
{
    [Fact]
    public void PickBestCandidate_ChoosesHighestVersion()
    {
        var lines = new[]
        {
            "/snap/bin/codex|codex-cli 0.114.0",
            "/home/u/.nvm/versions/node/v20.20.2/bin/codex|codex-cli 0.130.0",
            "/home/u/.nvm/versions/node/v22.15.0/bin/codex|codex-cli 0.144.4",
        };

        Assert.Equal("/home/u/.nvm/versions/node/v22.15.0/bin/codex", CliDetector.PickBestCandidate(lines));
    }

    [Fact]
    public void PickBestCandidate_DuplicatePaths_CountOnce()
    {
        var lines = new[]
        {
            "/usr/local/bin/codex|codex-cli 0.120.0",
            "/usr/local/bin/codex|codex-cli 0.120.0",
            "/snap/bin/codex|codex-cli 0.100.0",
        };

        Assert.Equal("/usr/local/bin/codex", CliDetector.PickBestCandidate(lines));
    }

    [Fact]
    public void PickBestCandidate_NoVersionOutput_StillReturnsAPath()
    {
        var lines = new[] { "/home/u/.local/bin/claude|" };

        Assert.Equal("/home/u/.local/bin/claude", CliDetector.PickBestCandidate(lines));
    }

    [Fact]
    public void PickBestCandidate_VersionedBeatsUnversioned()
    {
        var lines = new[]
        {
            "/a/broken|",
            "/b/works|codex-cli 0.1.0",
        };

        Assert.Equal("/b/works", CliDetector.PickBestCandidate(lines));
    }

    [Fact]
    public void PickBestCandidate_NoUsableLines_ReturnsNull()
    {
        Assert.Null(CliDetector.PickBestCandidate([]));
        Assert.Null(CliDetector.PickBestCandidate(["", "   ", "garbage without separator"]));
        Assert.Null(CliDetector.PickBestCandidate(["not-absolute|1.2.3"]));
    }

    [Fact]
    public void PickBestCandidate_ClaudeStyleVersionString_Parses()
    {
        var lines = new[] { "/home/u/.local/bin/claude|2.1.7 (Claude Code)" };

        Assert.Equal("/home/u/.local/bin/claude", CliDetector.PickBestCandidate(lines));
    }
}
