namespace Vivnest.Cloud.Auth;

public interface IApiKeyManagementService
{
    // Returns null if TenantId doesn't resolve to an existing, Active
    // Tenant, or SiteId doesn't resolve to an existing, Active Site under
    // it - see ApiKeyManagementService for why this wasn't enforced before
    // Tenant/Site existed as real entities (ADR-052).
    Task<ApiKeyCreationResult?> CreateAsync(
        string tenantId,
        string siteId,
        string? name,
        bool devicesOnly,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApiKeySummary>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        string keyId,
        CancellationToken cancellationToken = default);
}
