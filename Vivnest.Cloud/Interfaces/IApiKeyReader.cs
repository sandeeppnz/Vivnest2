using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IApiKeyReader
{
    Task<ApiKeyEntity?> GetByHashAsync(
        string keyHash,
        CancellationToken cancellationToken = default);
}
