using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Bridges.HomeAssistant;

public sealed class HomeAssistantCommandSender : IHomeAssistantCommandSender
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HomeAssistantCommandSender> _logger;

    public HomeAssistantCommandSender(
        HttpClient httpClient,
        IOptions<HomeAssistantOptions> options,
        ILogger<HomeAssistantCommandSender> logger)
    {
        var settings = options.Value;

        // Registered unconditionally for every Low-type agent (Program.cs),
        // not just ones with Home Assistant actually configured - same
        // "additive, never required" convention as IHomeAssistantConnectionTracker
        // and INetworkUsageTracker there. HomeAssistantWorker already checks
        // settings.Enabled before ever calling CallServiceAsync/GetStateAsync,
        // so a disabled/unconfigured integration just leaves BaseAddress null
        // instead of crashing the whole host on an empty BaseUrl.
        if (settings.Enabled && !string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            httpClient.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        }

        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task CallServiceAsync(
        string domain,
        string service,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/services/{domain}/{service}",
            new { entity_id = entityId },
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Home Assistant service call {Domain}.{Service} for {EntityId} failed: {Status} {Body}",
                domain,
                service,
                entityId,
                response.StatusCode,
                body);

            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation(
            "Home Assistant service call {Domain}.{Service} succeeded for {EntityId}.",
            domain,
            service,
            entityId);
    }

    public async Task<string?> GetStateAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/states/{entityId}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Home Assistant state lookup for {EntityId} failed: {Status}",
                entityId,
                response.StatusCode);

            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<HomeAssistantStateResponse>(
            cancellationToken: cancellationToken);

        return body?.State;
    }

    private sealed class HomeAssistantStateResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("state")]
        public string? State { get; set; }
    }
}
