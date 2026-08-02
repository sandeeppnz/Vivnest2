using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Services;

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

        httpClient.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.AccessToken);

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
}
