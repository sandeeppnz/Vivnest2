using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Repositories;

public class AzureTableDeviceTypeStore : IDeviceTypeStore
{
    private readonly AzureTableStore<DeviceTypeEntity> _store;

    public AzureTableDeviceTypeStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<DeviceTypeEntity>(
            tableServiceClient,
            tablesOptions.Value.DeviceTypes);
    }

    // No filter - this table is dedicated to device types, every row counts.
    public Task<IReadOnlyList<DeviceTypeEntity>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(cancellationToken: cancellationToken);
    }

    public Task<DeviceTypeEntity?> GetAsync(
        string deviceTypeId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(DeviceTypeEntity.PartitionKeyValue, deviceTypeId, cancellationToken);
    }

    public Task CreateAsync(
        DeviceTypeEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        DeviceTypeEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }

    public Task DeleteAsync(
        string deviceTypeId,
        CancellationToken cancellationToken = default)
    {
        return _store.DeleteAsync(DeviceTypeEntity.PartitionKeyValue, deviceTypeId, cancellationToken);
    }
}
