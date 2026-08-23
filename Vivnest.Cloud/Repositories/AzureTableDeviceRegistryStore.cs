using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Repositories;

public class AzureTableDeviceRegistryStore : IDeviceRegistryStore
{
    private readonly AzureTableStore<DeviceRegistryEntity> _store;

    public AzureTableDeviceRegistryStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<DeviceRegistryEntity>(
            tableServiceClient,
            tablesOptions.Value.DeviceRegistry);
    }

    private static string PartitionKey(string tenantId, string siteId) => $"{tenantId}|{siteId}";

    public Task<IReadOnlyList<DeviceRegistryEntity>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = PartitionKey(tenantId, siteId);

        return _store.QueryAsync(x => x.PartitionKey == partitionKey, cancellationToken);
    }

    public Task<DeviceRegistryEntity?> GetAsync(
        string tenantId,
        string siteId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(PartitionKey(tenantId, siteId), deviceId, cancellationToken);
    }

    public async Task<DeviceRegistryEntity?> GetByRuntimeDeviceIdAsync(
        string tenantId,
        string siteId,
        string runtimeDeviceId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = PartitionKey(tenantId, siteId);

        var results = await _store.QueryAsync(
            x => x.PartitionKey == partitionKey && x.RuntimeDeviceId == runtimeDeviceId,
            cancellationToken);

        return results.FirstOrDefault();
    }

    public Task CreateAsync(
        DeviceRegistryEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        DeviceRegistryEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }

    public Task DeleteAsync(
        string tenantId,
        string siteId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        return _store.DeleteAsync(PartitionKey(tenantId, siteId), deviceId, cancellationToken);
    }
}
