using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

public class AzureTableSiteStore : ISiteStore
{
    private readonly AzureTableStore<SiteEntity> _store;

    public AzureTableSiteStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<SiteEntity>(
            tableServiceClient,
            tablesOptions.Value.Sites);
    }

    public Task<SiteEntity?> GetAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(tenantId, siteId, cancellationToken);
    }

    // Partition-scoped ("get all Sites for Tenant X") - no full table scan.
    public Task<IReadOnlyList<SiteEntity>> GetByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(x => x.PartitionKey == tenantId, cancellationToken);
    }

    public Task CreateAsync(
        SiteEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        SiteEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }
}
