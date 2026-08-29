namespace Vivnest.Cloud.Api.Dtos;

public sealed record CreateApiKeyResponse(
    string KeyId,
    string ApiKey,
    string TenantId,
    string SiteId,
    string? Name,
    bool DevicesOnly,
    string Role,
    DateTime CreatedUtc);
