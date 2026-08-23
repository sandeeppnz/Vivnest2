using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IApiKeyStore
{
    Task<ApiKeyEntity?> GetByHashAsync(
        string keyHash,
        CancellationToken cancellationToken = default);

    Task<ApiKeyEntity?> GetByKeyIdAsync(
        string keyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApiKeyEntity>> GetByTenantAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        ApiKeyEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        ApiKeyEntity entity,
        CancellationToken cancellationToken = default);
}
