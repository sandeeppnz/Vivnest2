using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

public class AzureTableCapabilityDependencyStore : ICapabilityDependencyStore
{
    private readonly AzureTableStore<CapabilityDependencyEntity> _store;

    public AzureTableCapabilityDependencyStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<CapabilityDependencyEntity>(
            tableServiceClient,
            tablesOptions.Value.CapabilityDependencies);
    }

    // No filter - this table is dedicated to dependency edges, every row counts.
    public Task<IReadOnlyList<CapabilityDependencyEntity>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<CapabilityDependencyEntity>> ListByCapabilityAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(x => x.CapabilityId == capabilityId, cancellationToken);
    }

    public Task<CapabilityDependencyEntity?> GetAsync(
        string dependencyId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(CapabilityDependencyEntity.PartitionKeyValue, dependencyId, cancellationToken);
    }

    public Task CreateAsync(
        CapabilityDependencyEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task DeleteAsync(
        string dependencyId,
        CancellationToken cancellationToken = default)
    {
        return _store.DeleteAsync(CapabilityDependencyEntity.PartitionKeyValue, dependencyId, cancellationToken);
    }
}
