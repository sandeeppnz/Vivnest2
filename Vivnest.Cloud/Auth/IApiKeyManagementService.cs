namespace Vivnest.Cloud.Auth;

public interface IApiKeyManagementService
{
    Task<ApiKeyCreationResult> CreateAsync(
        string tenantId,
        string siteId,
        string? name,
        CancellationToken cancellationToken = default);
}
