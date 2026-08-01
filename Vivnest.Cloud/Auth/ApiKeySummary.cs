namespace Vivnest.Cloud.Auth;

// Deliberately excludes the key hash/material - this is what an admin sees
// when listing keys, never anything that could be used to authenticate.
public sealed record ApiKeySummary(
    string KeyId,
    string? Name,
    string TenantId,
    string SiteId,
    bool Enabled,
    DateTime CreatedUtc);
