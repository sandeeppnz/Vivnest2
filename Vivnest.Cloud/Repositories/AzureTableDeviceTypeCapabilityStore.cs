using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Cloud.Repositories;

public class AzureTableDeviceTypeCapabilityStore : IDeviceTypeCapabilityStore
{
    private readonly AzureTableStore<DeviceTypeCapabilityEntity> _store;

    public AzureTableDeviceTypeCapabilityStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<DeviceTypeCapabilityEntity>(
            tableServiceClient,
            tablesOptions.Value.DeviceTypeCapabilities);
    }

    // No filter - this table is dedicated to compatibility rows, every row counts.
    public Task<IReadOnlyList<DeviceTypeCapabilityEntity>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<DeviceTypeCapabilityEntity>> ListByDeviceTypeAsync(
        string deviceTypeId,
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(x => x.DeviceTypeId == deviceTypeId, cancellationToken);
    }

    public Task<DeviceTypeCapabilityEntity?> GetAsync(
        string deviceTypeCapabilityId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(DeviceTypeCapabilityEntity.PartitionKeyValue, deviceTypeCapabilityId, cancellationToken);
    }

    public Task CreateAsync(
        DeviceTypeCapabilityEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task DeleteAsync(
        string deviceTypeCapabilityId,
        CancellationToken cancellationToken = default)
    {
        return _store.DeleteAsync(DeviceTypeCapabilityEntity.PartitionKeyValue, deviceTypeCapabilityId, cancellationToken);
    }
}
