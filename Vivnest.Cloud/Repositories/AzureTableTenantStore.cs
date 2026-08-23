using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Repositories;

public class AzureTableTenantStore : ITenantStore
{
    private readonly AzureTableStore<TenantEntity> _store;

    public AzureTableTenantStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<TenantEntity>(
            tableServiceClient,
            tablesOptions.Value.Tenants);
    }

    // No filter - this table is dedicated to tenants, every row counts.
    public Task<IReadOnlyList<TenantEntity>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(cancellationToken: cancellationToken);
    }

    public Task<TenantEntity?> GetAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(TenantEntity.PartitionKeyValue, tenantId, cancellationToken);
    }

    public Task CreateAsync(
        TenantEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        TenantEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }
}
