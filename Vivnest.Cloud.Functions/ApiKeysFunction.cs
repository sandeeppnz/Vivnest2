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

    // AuthorizationLevel.Function everywhere in this file: gated by an Azure
    // Functions host key, not the tenant x-api-key scheme the read endpoints
    // use. Creating, listing, and revoking keys are privileged, operator-only
    // actions - none of them must be reachable with just a tenant read key,
    // or any caller with one key could see or kill every other key.
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
            body.DevicesOnly,
            cancellationToken);

        if (result == null)
        {
            return new BadRequestObjectResult(
                $"TenantId \"{body.TenantId}\" and SiteId \"{body.SiteId}\" must reference an existing, " +
                "Active Tenant and Site.");
        }

        return new OkObjectResult(new CreateApiKeyResponse(
            result.KeyId,
            result.ApiKey,
            body.TenantId,
            body.SiteId,
            body.Name,
            body.DevicesOnly,
            result.CreatedUtc));
    }

    [Function(nameof(ListApiKeys))]
    public async Task<IActionResult> ListApiKeys(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "apikeys")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = request.Query["tenantId"].ToString();
        var siteId = request.Query["siteId"].ToString();

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(siteId))
        {
            return new BadRequestObjectResult("tenantId and siteId query parameters are required.");
        }

        var keys = await _apiKeyManagement.ListAsync(tenantId, siteId, cancellationToken);

        return new OkObjectResult(keys);
    }

    [Function(nameof(RevokeApiKey))]
    public async Task<IActionResult> RevokeApiKey(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "apikeys/{keyId}/revoke")]
            HttpRequest request,
        string keyId,
        CancellationToken cancellationToken)
    {
        var revoked = await _apiKeyManagement.RevokeAsync(keyId, cancellationToken);

        if (!revoked)
            return new NotFoundResult();

        return new OkResult();
    }
}
