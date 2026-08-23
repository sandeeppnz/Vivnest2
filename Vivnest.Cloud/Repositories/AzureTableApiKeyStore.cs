using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Cloud.Repositories;

public class AzureTableApiKeyStore : IApiKeyStore
{
    private const string InfoRowKey = "info";

    private readonly AzureTableStore<ApiKeyEntity> _store;

    public AzureTableApiKeyStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<ApiKeyEntity>(
            tableServiceClient,
            tablesOptions.Value.ApiKeys);
    }

    public Task<ApiKeyEntity?> GetByHashAsync(
        string keyHash,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(keyHash, InfoRowKey, cancellationToken);
    }

    public async Task<ApiKeyEntity?> GetByKeyIdAsync(
        string keyId,
        CancellationToken cancellationToken = default)
    {
        var results = await _store.QueryAsync(
            x => x.KeyId == keyId,
            cancellationToken);

        return results.FirstOrDefault();
    }

    public Task<IReadOnlyList<ApiKeyEntity>> GetByTenantAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(
            x => x.TenantId == tenantId && x.SiteId == siteId,
            cancellationToken);
    }

    public Task CreateAsync(
        ApiKeyEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        ApiKeyEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }
}
