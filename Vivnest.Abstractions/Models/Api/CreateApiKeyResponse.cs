namespace Vivnest.Abstractions.Models.Api;

public sealed record CreateApiKeyResponse(
    string KeyId,
    string ApiKey,
    string TenantId,
    string SiteId,
    string? Name,
    bool DevicesOnly,
    DateTime CreatedUtc);
