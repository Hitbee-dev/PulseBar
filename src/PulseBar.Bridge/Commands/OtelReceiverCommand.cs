using System.Net;
using System.Text.Json;
using PulseBar.Providers.Claude.OpenTelemetry;

namespace PulseBar.Bridge.Commands;

/// <summary>
/// `PulseBar.Bridge otel-receiver --listen 127.0.0.1:4318 --queue <jsonl-path>`
///
/// WSL helper (spec §10.6): listens on WSL loopback for Claude Code's OTLP JSON
/// logs, normalizes token events, and appends them to a shared JSONL queue on
/// /mnt/c that the Windows app ingests. The bearer secret arrives on stdin
/// (first line) so it never shows up in `ps`.
/// </summary>
public static class OtelReceiverCommand
{
    private const int MaxPayloadBytes = 5 * 1024 * 1024;
    private const long MaxQueueBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static int Run(string[] args, TextReader stdin, TextWriter stderr, CancellationToken cancellationToken)
    {
        string listen = "127.0.0.1:4318";
        string? queuePath = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--listen":
                    listen = args[++i];
                    break;
                case "--queue":
                    queuePath = args[++i];
                    break;
            }
        }

        if (queuePath is null)
        {
            stderr.WriteLine("otel-receiver: --queue is required.");
            return 0;
        }

        var secret = stdin.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(secret))
        {
            stderr.WriteLine("otel-receiver: expected bearer secret on stdin.");
            return 0;
        }

        if (!listen.StartsWith("127.0.0.1", StringComparison.Ordinal))
        {
            stderr.WriteLine("otel-receiver: only loopback listening is allowed.");
            return 0;
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://{listen}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            stderr.WriteLine($"otel-receiver: cannot listen on {listen} ({ex.Message}).");
            return 0;
        }

        stderr.WriteLine($"otel-receiver: listening on http://{listen}/v1/logs");
        using var registration = cancellationToken.Register(listener.Stop);

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = listener.GetContext();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }

            try
            {
                Handle(context, secret, queuePath);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"otel-receiver: request failed ({ex.GetType().Name}).");
            }
        }

        return 0;
    }

    private static void Handle(HttpListenerContext context, string secret, string queuePath)
    {
        var request = context.Request;
        var response = context.Response;

        if (!IPAddress.IsLoopback(request.RemoteEndPoint.Address)
            || request.Headers["Authorization"] != $"Bearer {secret}")
        {
            response.StatusCode = 401;
            response.Close();
            return;
        }

        if (request.HttpMethod != "POST" || request.Url?.AbsolutePath != "/v1/logs")
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        if (request.ContentLength64 > MaxPayloadBytes
            || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            response.StatusCode = 415;
            response.Close();
            return;
        }

        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            body = reader.ReadToEnd();
        }

        var events = OtelLogsParser.Parse(body, profileId: "claude-otel-wsl");
        if (events.Count > 0)
        {
            AppendToQueue(queuePath, events);
        }

        var ok = "{}"u8.ToArray();
        response.StatusCode = 200;
        response.ContentType = "application/json";
        response.OutputStream.Write(ok);
        response.Close();
    }

    private static void AppendToQueue(string queuePath, IReadOnlyList<Core.Models.TokenUsageEvent> events)
    {
        var directory = Path.GetDirectoryName(queuePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Hard cap: when the Windows app isn't draining the queue, stop growing it.
        if (File.Exists(queuePath) && new FileInfo(queuePath).Length > MaxQueueBytes)
        {
            return;
        }

        using var stream = new FileStream(queuePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        foreach (var tokenEvent in events)
        {
            writer.WriteLine(JsonSerializer.Serialize(tokenEvent, JsonOptions));
        }
    }
}
