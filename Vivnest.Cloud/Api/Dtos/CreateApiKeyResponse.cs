namespace Vivnest.Cloud.Api.Dtos;

public sealed record CreateApiKeyResponse(
    string ApiKey,
    string TenantId,
    string SiteId,
    string? Name,
    DateTime CreatedUtc);
