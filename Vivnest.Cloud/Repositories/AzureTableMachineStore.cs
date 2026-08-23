using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Domain.Sites;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Cloud.Repositories;

public class AzureTableMachineStore : IMachineStore
{
    private readonly AzureTableStore<MachineEntity> _store;

    public AzureTableMachineStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<MachineEntity>(
            tableServiceClient,
            tablesOptions.Value.Machines);
    }

    public Task<IReadOnlyList<MachineEntity>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;

        return _store.QueryAsync(x => x.PartitionKey == partitionKey, cancellationToken);
    }

    public Task<MachineEntity?> GetAsync(
        string tenantId,
        string siteId,
        string machineId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(new SiteScope(tenantId, siteId).PartitionKey, machineId, cancellationToken);
    }

    public Task CreateAsync(
        MachineEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        MachineEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }
}
