using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PulseBar.Providers.Codex.AppServer;

public sealed record JsonRpcNotification(string Method, JsonElement? Params);

public sealed class JsonRpcException : Exception
{
    public JsonRpcException(int code, string message)
        : base($"JSON-RPC error {code}: {message}")
    {
        Code = code;
    }

    public int Code { get; }
}

/// <summary>
/// Minimal line-delimited JSON-RPC 2.0 client over stdio streams.
/// Codex app-server responses omit the "jsonrpc" field, so only "id"/"result"/
/// "error"/"method" are relied upon. Malformed lines are logged and skipped.
/// </summary>
public sealed class JsonRpcClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readLoop;
    private long _nextId;

    public JsonRpcClient(TextReader reader, TextWriter writer, ILogger logger)
    {
        _reader = reader;
        _writer = writer;
        _logger = logger;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public event EventHandler<JsonRpcNotification>? NotificationReceived;

    /// <summary>Raised once when the peer closes the stream or the read loop dies.</summary>
    public event EventHandler? Disconnected;

    public async Task<JsonElement> InvokeAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var payload = JsonSerializer.Serialize(
                new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } },
                WriteOptions);
            await WriteLineAsync(payload, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await using var registration = timeoutCts.Token.Register(
                () => tcs.TrySetException(new TimeoutException($"JSON-RPC call '{method}' timed out.")));

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", method, @params = parameters ?? new { } },
            WriteOptions);
        return WriteLineAsync(payload, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        FailAllPending(new ObjectDisposedException(nameof(JsonRpcClient)));
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The loop's own errors were already logged.
        }

        _cts.Dispose();
        _writeLock.Dispose();
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                ProcessLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JSON-RPC read loop terminated.");
        }
        finally
        {
            FailAllPending(new IOException("app-server connection closed."));
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ProcessLine(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring malformed JSON-RPC line ({Length} chars).", line.Length);
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.TryGetProperty("id", out var idElement)
                && idElement.ValueKind == JsonValueKind.Number
                && idElement.TryGetInt64(out var id)
                && _pending.TryGetValue(id, out var tcs))
            {
                if (root.TryGetProperty("error", out var error))
                {
                    var code = error.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                    var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    tcs.TrySetException(new JsonRpcException(code, message));
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    tcs.TrySetResult(result.Clone());
                }
                else
                {
                    tcs.TrySetException(new JsonRpcException(0, "Response had neither result nor error."));
                }

                return;
            }

            if (root.TryGetProperty("method", out var methodElement))
            {
                var method = methodElement.GetString() ?? "";
                JsonElement? parameters = root.TryGetProperty("params", out var p) ? p.Clone() : null;
                NotificationReceived?.Invoke(this, new JsonRpcNotification(method, parameters));
            }
        }
    }

    private void FailAllPending(Exception exception)
    {
        foreach (var (id, tcs) in _pending)
        {
            tcs.TrySetException(exception);
            _pending.TryRemove(id, out _);
        }
    }
}
