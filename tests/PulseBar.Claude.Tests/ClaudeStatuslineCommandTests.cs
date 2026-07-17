using System.Text.Json.Nodes;
using PulseBar.Bridge.Commands;

namespace PulseBar.Claude.Tests;

public sealed class ClaudeStatuslineCommandTests : IDisposable
{
    private readonly string _dir;
    private readonly string _cachePath;

    public ClaudeStatuslineCommandTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PulseBarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _cachePath = Path.Combine(_dir, "claude-status.json");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private int Run(string stdinJson, params string[] extraArgs)
    {
        string[] args = ["--output", _cachePath, .. extraArgs];
        return ClaudeStatuslineCommand.Run(
            args, new StringReader(stdinJson), TextWriter.Null, TextWriter.Null);
    }

    [Fact]
    public void Run_ValidStatusline_WritesNormalizedCache()
    {
        var exit = Run("""
            {"model":{"id":"claude-fable-5","display_name":"Fable 5"},
             "rate_limits":{"five_hour":{"used_percentage":41,"resets_at":1784269200}}}
            """);

        Assert.Equal(0, exit);
        var cache = JsonNode.Parse(File.ReadAllText(_cachePath))!;
        Assert.Equal("claude", cache["provider"]!.GetValue<string>());
        Assert.Equal(41, cache["rateLimits"]!["fiveHour"]!["usedPercent"]!.GetValue<double>());
    }

    [Fact]
    public void Run_GarbageInput_StillExitsZero_NoCacheWritten()
    {
        var exit = Run("total garbage");

        Assert.Equal(0, exit);
        Assert.False(File.Exists(_cachePath));
    }

    [Fact]
    public void Run_NoTempFileLeftBehind()
    {
        Run("""{"rate_limits":{"five_hour":{"used_percentage":1,"resets_at":2}}}""");

        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Run_MissingOutputArg_ExitsZero()
    {
        var exit = ClaudeStatuslineCommand.Run(
            [], new StringReader("{}"), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Run_Passthrough_EchoesWrappedCommandOutput()
    {
        var stdout = new StringWriter();
        var command = OperatingSystem.IsWindows() ? "echo hud-output" : "echo hud-output";

        var exit = ClaudeStatuslineCommand.Run(
            ["--output", _cachePath, "--passthrough", command],
            new StringReader("""{"rate_limits":{"five_hour":{"used_percentage":5,"resets_at":9}}}"""),
            stdout,
            TextWriter.Null);

        Assert.Equal(0, exit);
        Assert.Contains("hud-output", stdout.ToString());
        Assert.True(File.Exists(_cachePath));
    }
}
