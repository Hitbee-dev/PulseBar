using System.IO;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Configuration;
using PulseBar.Providers.Claude.OpenTelemetry;
using PulseBar.Storage.Sqlite;
using PulseBar.Windows.Security;

namespace PulseBar.App.Services;

/// <summary>
/// Loopback OTLP/HTTP receiver for Claude Code telemetry (spec §10.5).
/// Binds 127.0.0.1 only, requires the DPAPI-protected bearer secret, accepts
/// the OTLP JSON encoding on /v1/logs, caps payload size, and stores only
/// normalized token events.
/// </summary>
public sealed class OtelReceiverService : BackgroundService
{
    private const int MaxPayloadBytes = 5 * 1024 * 1024;
    private static readonly int[] CandidatePorts = [4318, 4319, 4320, 4321];

    private readonly IAppPaths _paths;
    private readonly ITokenUsageRepository _repository;
    private readonly ILogger<OtelReceiverService> _logger;

    public OtelReceiverService(
        IAppPaths paths,
        ITokenUsageRepository repository,
        ILogger<OtelReceiverService> logger)
    {
        _paths = paths;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Raised after new events land in storage (UI refresh trigger).</summary>
    public event EventHandler? EventsIngested;

    /// <summary>Null until the listener is up.</summary>
    public string? Endpoint { get; private set; }

    public string? Secret { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        Secret = OtelSecretStore.GetOrCreate(Path.Combine(_paths.BridgeDir, "otel.secret"));

        HttpListener? listener = null;
        foreach (var port in CandidatePorts)
        {
            var candidate = new HttpListener();
            candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                candidate.Start();
                listener = candidate;
                Endpoint = $"http://127.0.0.1:{port}";
                break;
            }
            catch (HttpListenerException)
            {
                candidate.Close();
            }
        }

        if (listener is null)
        {
            _logger.LogWarning("No loopback port available for the OTLP receiver; Fable telemetry disabled.");
            return;
        }

        _logger.LogInformation("OTLP receiver listening on {Endpoint}/v1/logs.", Endpoint);

        await using var registration = stoppingToken.Register(listener.Stop);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _ = Task.Run(() => HandleRequest(context), CancellationToken.None);
            }
        }
        finally
        {
            listener.Close();
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            if (!IPAddress.IsLoopback(request.RemoteEndPoint.Address))
            {
                response.StatusCode = 403;
                response.Close();
                return;
            }

            if (request.Headers["Authorization"] != $"Bearer {Secret}")
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

            if (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            {
                // Only the OTLP JSON encoding is supported (OTEL_EXPORTER_OTLP_PROTOCOL=http/json).
                response.StatusCode = 415;
                response.Close();
                return;
            }

            if (request.ContentLength64 > MaxPayloadBytes)
            {
                response.StatusCode = 413;
                response.Close();
                return;
            }

            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                body = reader.ReadToEnd();
            }

            var events = OtelLogsParser.Parse(body, profileId: "claude-otel-windows");
            if (events.Count > 0)
            {
                var inserted = _repository.UpsertEvents(events);
                if (inserted > 0)
                {
                    EventsIngested?.Invoke(this, EventArgs.Empty);
                }
            }

            var ok = "{}"u8.ToArray();
            response.StatusCode = 200;
            response.ContentType = "application/json";
            response.OutputStream.Write(ok);
            response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OTLP request handling failed.");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch (Exception)
            {
            }
        }
    }
}
