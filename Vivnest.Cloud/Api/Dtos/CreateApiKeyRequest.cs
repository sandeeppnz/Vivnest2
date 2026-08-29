namespace Vivnest.Cloud.Api.Dtos;

// Role: "developer" | "user" (ApiKeyRoles) - defaults to developer,
// matching what legacy keys (created before roles existed) resolve to.
public sealed record CreateApiKeyRequest(
    string TenantId,
    string SiteId,
    string? Name,
    bool DevicesOnly = false,
    string? Role = null);
