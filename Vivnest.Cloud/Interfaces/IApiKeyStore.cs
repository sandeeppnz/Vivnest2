using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IApiKeyStore
{
    Task<ApiKeyEntity?> GetByHashAsync(
        string keyHash,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        ApiKeyEntity entity,
        CancellationToken cancellationToken = default);
}
