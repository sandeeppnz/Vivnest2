using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions;

public class ApiKeysFunction
{
    private readonly IApiKeyManagementService _apiKeyManagement;

    public ApiKeysFunction(IApiKeyManagementService apiKeyManagement)
    {
        _apiKeyManagement = apiKeyManagement;
    }

    // AuthorizationLevel.Function: gated by an Azure Functions host key, not
    // the tenant x-api-key scheme the read endpoints use. Minting a key is a
    // privileged, operator-only action - it must not be reachable with just
    // any valid tenant key, or any anonymous caller could mint one for a
    // tenant of their choosing.
    [Function(nameof(CreateApiKey))]
    public async Task<IActionResult> CreateApiKey(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "apikeys")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        CreateApiKeyRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<CreateApiKeyRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null
            || string.IsNullOrWhiteSpace(body.TenantId)
            || string.IsNullOrWhiteSpace(body.SiteId))
        {
            return new BadRequestObjectResult("TenantId and SiteId are required.");
        }

        var result = await _apiKeyManagement.CreateAsync(
            body.TenantId,
            body.SiteId,
            body.Name,
            cancellationToken);

        return new OkObjectResult(new CreateApiKeyResponse(
            result.ApiKey,
            body.TenantId,
            body.SiteId,
            body.Name,
            result.CreatedUtc));
    }
}
