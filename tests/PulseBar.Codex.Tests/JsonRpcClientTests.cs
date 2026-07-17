using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PulseBar.Providers.Codex.AppServer;

namespace PulseBar.Codex.Tests;

public sealed class JsonRpcClientTests : IAsyncDisposable
{
    // Client writes → _clientOut; test feeds server lines through _serverIn.
    private readonly StringWriter _clientOut = new();
    private readonly ServerStream _serverIn = new();
    private readonly JsonRpcClient _client;

    public JsonRpcClientTests()
    {
        _client = new JsonRpcClient(_serverIn.Reader, _clientOut, NullLogger.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _serverIn.Complete();
    }

    [Fact]
    public async Task InvokeAsync_MatchesResponseById()
    {
        var call = _client.InvokeAsync("account/read", null, TimeSpan.FromSeconds(5), CancellationToken.None);
        _serverIn.Feed("""{"id":1,"result":{"ok":true}}""");

        var result = await call;

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Contains("\"method\":\"account/read\"", _clientOut.ToString());
    }

    [Fact]
    public async Task InvokeAsync_ErrorResponse_ThrowsJsonRpcException()
    {
        var call = _client.InvokeAsync("bad/method", null, TimeSpan.FromSeconds(5), CancellationToken.None);
        _serverIn.Feed("""{"id":1,"error":{"code":-32601,"message":"method not found"}}""");

        var ex = await Assert.ThrowsAsync<JsonRpcException>(() => call);

        Assert.Equal(-32601, ex.Code);
    }

    [Fact]
    public async Task InvokeAsync_Timeout_Throws()
    {
        var call = _client.InvokeAsync("slow", null, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() => call);
    }

    [Fact]
    public async Task MalformedLines_AreIsolated_LaterResponsesStillWork()
    {
        var call = _client.InvokeAsync("x", null, TimeSpan.FromSeconds(5), CancellationToken.None);
        _serverIn.Feed("this is not json {{{");
        _serverIn.Feed("""{"id":1,"result":{"ok":1}}""");

        var result = await call;

        Assert.Equal(1, result.GetProperty("ok").GetInt32());
    }

    [Fact]
    public async Task Notifications_AreRaisedWithMethodAndParams()
    {
        var received = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.NotificationReceived += (_, n) => received.TrySetResult(n);

        _serverIn.Feed("""{"method":"account/rateLimits/updated","params":{"x":1}}""");

        var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("account/rateLimits/updated", notification.Method);
        Assert.Equal(1, notification.Params!.Value.GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task StreamClosed_FailsPendingCalls()
    {
        var call = _client.InvokeAsync("x", null, TimeSpan.FromSeconds(30), CancellationToken.None);
        _serverIn.Complete();

        await Assert.ThrowsAsync<IOException>(() => call);
    }

    /// <summary>A TextReader the test can feed lines into (blocking pipe semantics).</summary>
    private sealed class ServerStream
    {
        private readonly System.IO.Pipelines.Pipe _pipe = new();

        public TextReader Reader
            => new StreamReader(_pipe.Reader.AsStream(), Encoding.UTF8);

        public void Feed(string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            _pipe.Writer.WriteAsync(bytes).AsTask().GetAwaiter().GetResult();
            _pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
        }

        public void Complete() => _pipe.Writer.Complete();
    }
}
