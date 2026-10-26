using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Entities;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Options;

namespace Vivnest.Cloud.Notifications;

// Sends through Expo's push API, which fans out to FCM (Android) and APNs
// (iOS) - see ADR-129 for why that rather than FCM directly.
public sealed class ExpoPushSender : IPushAlertSender
{
    // Expo's documented per-request limit.
    private const int BatchSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ExpoPushOptions _options;
    private readonly IPushDeviceStore _devices;
    private readonly ILogger<ExpoPushSender> _logger;

    public ExpoPushSender(
        HttpClient http,
        IOptions<ExpoPushOptions> options,
        IPushDeviceStore devices,
        ILogger<ExpoPushSender> logger)
    {
        _http = http;
        _options = options.Value;
        _devices = devices;
        _logger = logger;
    }

    public async Task<int> SendAsync(
        IReadOnlyCollection<PushDeviceEntity> devices,
        PushAlert alert,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || devices.Count == 0)
            return 0;

        var accepted = 0;

        foreach (var batch in devices.Chunk(BatchSize))
        {
            try
            {
                accepted += await SendBatchAsync(batch, alert, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(
                    ex,
                    "Push delivery of \"{Title}\" to {Count} phone(s) failed.",
                    alert.Title,
                    batch.Length);
            }
        }

        return accepted;
    }

    private async Task<int> SendBatchAsync(
        PushDeviceEntity[] batch,
        PushAlert alert,
        CancellationToken cancellationToken)
    {
        var messages = batch.Select(d => new ExpoMessage(
            To: d.ExpoPushToken,
            Title: alert.Title,
            Body: alert.Body,
            Sound: "default",
            Priority: alert.HighPriority ? "high" : "default",
            ChannelId: alert.ChannelId,
            Ttl: (int)alert.TimeToLive.TotalSeconds,
            Data: alert.Data)).ToList();

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = JsonContent.Create(messages, options: JsonOptions),
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogError(
                "Expo push API returned {Status}: {Body}",
                (int)response.StatusCode,
                body.Length > 300 ? body[..300] : body);

            return 0;
        }

        var result = await response.Content.ReadFromJsonAsync<ExpoResponse>(JsonOptions, cancellationToken);
        var tickets = result?.Data ?? [];

        var ok = 0;

        // Tickets come back in request order.
        for (var i = 0; i < tickets.Count && i < batch.Length; i++)
        {
            var ticket = tickets[i];

            if (string.Equals(ticket.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                ok++;
                continue;
            }

            if (string.Equals(ticket.Details?.Error, "DeviceNotRegistered", StringComparison.Ordinal))
            {
                // The app was uninstalled or its token rotated. Keeping the
                // row would fail on every alert from now on.
                _logger.LogWarning(
                    "Push token for \"{Name}\" is no longer registered with Expo; removing it.",
                    batch[i].Name);

                await _devices.DeleteAsync(
                    batch[i].TenantId, batch[i].SiteId, batch[i].RowKey, cancellationToken);

                continue;
            }

            _logger.LogWarning(
                "Expo rejected a push for \"{Name}\": {Error} {Message}",
                batch[i].Name,
                ticket.Details?.Error,
                ticket.Message);
        }

        return ok;
    }

    private sealed record ExpoMessage(
        string To,
        string Title,
        string Body,
        string Sound,
        string Priority,
        string ChannelId,
        int Ttl,
        IReadOnlyDictionary<string, string>? Data);

    private sealed class ExpoResponse
    {
        public List<ExpoTicket>? Data { get; set; }
    }

    private sealed class ExpoTicket
    {
        public string? Status { get; set; }
        public string? Message { get; set; }
        public ExpoTicketDetails? Details { get; set; }
    }

    private sealed class ExpoTicketDetails
    {
        public string? Error { get; set; }
    }
}
