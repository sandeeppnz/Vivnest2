namespace Vivnest.Abstractions.Models.Api;

public sealed record CreateApiKeyRequest(
    string TenantId,
    string SiteId,
    string? Name,
    bool DevicesOnly = false);
