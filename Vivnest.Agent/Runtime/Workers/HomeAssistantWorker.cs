using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Runtime.Workers;

// Long-lived WebSocket subscriber, not a polling loop like
// CameraCaptureWorker/SmartPlugMonitorWorker - Home Assistant pushes
// state_changed events over /api/websocket, so there's nothing to poll.
// Reconnects with a fixed delay on any failure (auth rejection, socket
// drop, HA restart) rather than giving up.
public sealed class HomeAssistantWorker : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);

    // A connection that's had no traffic in a while can go silently stale
    // through Docker Desktop's WSL2 port-forwarding (server pushes stop
    // arriving with no exception raised) - periodic pings keep the path
    // warm, and the receive timeout forces a reconnect if it ever does.
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(45);

    private readonly IEventDispatcher _dispatcher;
    private readonly HomeAssistantOptions _options;
    private readonly ILogger<HomeAssistantWorker> _logger;

    public HomeAssistantWorker(
        IEventDispatcher dispatcher,
        IOptions<HomeAssistantOptions> options,
        ILogger<HomeAssistantWorker> logger)
    {
        _dispatcher = dispatcher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Home Assistant integration disabled. Home Assistant Worker has nothing to do.");

            return;
        }

        if (!_options.Entities.Any(e => e.Enabled))
        {
            _logger.LogWarning(
                "No enabled Home Assistant entities configured. Home Assistant Worker has nothing to do.");

            return;
        }

        _logger.LogInformation("Home Assistant Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Home Assistant connection failed. Reconnecting in {Delay}.",
                    ReconnectDelay);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task RunConnectionAsync(CancellationToken stoppingToken)
    {
        var wsUri = BuildWebSocketUri(_options.BaseUrl);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(wsUri, stoppingToken);

        _logger.LogInformation("Connected to Home Assistant at {Uri}.", wsUri);

        await AuthenticateAsync(socket, stoppingToken);
        await SubscribeToStateChangesAsync(socket, stoppingToken);

        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var pingTask = SendPeriodicPingsAsync(socket, pingCts.Token);

        try
        {
            var buffer = new byte[16 * 1024];

            while (socket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    receiveCts.CancelAfter(ReceiveTimeout);

                    try
                    {
                        result = await socket.ReceiveAsync(buffer, receiveCts.Token);
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"No data received from Home Assistant within {ReceiveTimeout}; treating connection as stale.");
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning(
                            "Home Assistant closed the connection: {Status} {Description}",
                            result.CloseStatus,
                            result.CloseStatusDescription);

                        return;
                    }

                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                stream.Position = 0;

                await HandleMessageAsync(stream, stoppingToken);
            }
        }
        finally
        {
            pingCts.Cancel();

            try
            {
                await pingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task SendPeriodicPingsAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var id = 1000;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(PingInterval, cancellationToken);

            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                await SendJsonAsync(
                    socket,
                    new { id = id++, type = "ping" },
                    cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Failed to send Home Assistant keepalive ping.");
                return;
            }
        }
    }

    private static Uri BuildWebSocketUri(string baseUrl)
    {
        var uri = new Uri(baseUrl, UriKind.Absolute);
        var scheme = uri.Scheme == "https" ? "wss" : "ws";

        return new UriBuilder(uri)
        {
            Scheme = scheme,
            Path = "/api/websocket"
        }.Uri;
    }

    private async Task AuthenticateAsync(
        ClientWebSocket socket,
        CancellationToken stoppingToken)
    {
        using var authRequired = await ReceiveJsonAsync(socket, stoppingToken);

        if (authRequired.RootElement.GetProperty("type").GetString() != "auth_required")
        {
            throw new InvalidOperationException(
                "Unexpected message from Home Assistant; expected auth_required.");
        }

        await SendJsonAsync(
            socket,
            new { type = "auth", access_token = _options.AccessToken },
            stoppingToken);

        using var authResult = await ReceiveJsonAsync(socket, stoppingToken);
        var authType = authResult.RootElement.GetProperty("type").GetString();

        if (authType != "auth_ok")
        {
            throw new InvalidOperationException(
                $"Home Assistant authentication failed: {authType}");
        }

        _logger.LogInformation("Authenticated with Home Assistant.");
    }

    private async Task SubscribeToStateChangesAsync(
        ClientWebSocket socket,
        CancellationToken stoppingToken)
    {
        await SendJsonAsync(
            socket,
            new { id = 1, type = "subscribe_events", event_type = "state_changed" },
            stoppingToken);

        using var subscribeResult = await ReceiveJsonAsync(socket, stoppingToken);

        if (!subscribeResult.RootElement.TryGetProperty("success", out var success) ||
            !success.GetBoolean())
        {
            throw new InvalidOperationException(
                "Home Assistant rejected the state_changed subscription.");
        }

        _logger.LogInformation(
            "Subscribed to state_changed events for {Count} enabled entities ({Total} configured).",
            _options.Entities.Count(e => e.Enabled),
            _options.Entities.Count);
    }

    private async Task HandleMessageAsync(Stream stream, CancellationToken stoppingToken)
    {
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: stoppingToken);
        var root = doc.RootElement;

        if (root.GetProperty("type").GetString() != "event")
        {
            return;
        }

        var eventElement = root.GetProperty("event");

        if (eventElement.GetProperty("event_type").GetString() != "state_changed")
        {
            return;
        }

        var data = eventElement.GetProperty("data");
        var entityId = data.GetProperty("entity_id").GetString();

        var mapping = _options.Entities.FirstOrDefault(e =>
            string.Equals(e.EntityId, entityId, StringComparison.OrdinalIgnoreCase));

        if (mapping is null || !mapping.Enabled)
        {
            return;
        }

        if (!data.TryGetProperty("new_state", out var newStateElement) ||
            newStateElement.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var newState = newStateElement.GetProperty("state").GetString();
        string? previousState = null;

        if (data.TryGetProperty("old_state", out var oldStateElement) &&
            oldStateElement.ValueKind != JsonValueKind.Null)
        {
            previousState = oldStateElement.GetProperty("state").GetString();
        }

        var attributes = new Dictionary<string, string?>();

        if (newStateElement.TryGetProperty("attributes", out var attributesElement) &&
            attributesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var attribute in attributesElement.EnumerateObject())
            {
                attributes[attribute.Name] = attribute.Value.ToString();
            }
        }

        _logger.LogInformation(
            "Home Assistant state change for {EntityId}: {Previous} -> {New}",
            entityId,
            previousState,
            newState);

        await _dispatcher.PublishAsync(
            new HomeAssistantStateChangedEvent(
                entityId!,
                mapping.DeviceId,
                mapping.DeviceType,
                mapping.EventType,
                newState,
                previousState,
                attributes,
                DateTime.UtcNow),
            stoppingToken);
    }

    private static async Task SendJsonAsync(
        ClientWebSocket socket,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        stream.Position = 0;

        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
