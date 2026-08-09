using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

public class AzureTableCapabilityStore : ICapabilityStore
{
    private readonly AzureTableStore<CapabilityEntity> _store;

    public AzureTableCapabilityStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<CapabilityEntity>(
            tableServiceClient,
            tablesOptions.Value.Capabilities);
    }

    // No filter - this table is dedicated to capabilities, every row counts.
    public Task<IReadOnlyList<CapabilityEntity>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(cancellationToken: cancellationToken);
    }

    public Task<CapabilityEntity?> GetAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(CapabilityEntity.PartitionKeyValue, capabilityId, cancellationToken);
    }

    public Task CreateAsync(
        CapabilityEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        CapabilityEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }

    public Task DeleteAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        return _store.DeleteAsync(CapabilityEntity.PartitionKeyValue, capabilityId, cancellationToken);
    }
}
