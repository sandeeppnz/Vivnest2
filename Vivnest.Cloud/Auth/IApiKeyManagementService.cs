namespace Vivnest.Cloud.Auth;

public interface IApiKeyManagementService
{
    Task<ApiKeyCreationResult> CreateAsync(
        string tenantId,
        string siteId,
        string? name,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApiKeySummary>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        string keyId,
        CancellationToken cancellationToken = default);
}
