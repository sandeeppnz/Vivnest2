using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Repositories;

public class AzureTableDeviceSnapshotStateReader : IDeviceSnapshotStateReader
{
    private readonly AzureTableStore<DeviceSnapshotStateEntity> _store;

    public AzureTableDeviceSnapshotStateReader(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<DeviceSnapshotStateEntity>(
            tableServiceClient,
            tablesOptions.Value.DeviceSnapshotState);
    }

    public Task<DeviceSnapshotStateEntity?> GetAsync(
        string tenantId,
        string siteId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(
            $"{tenantId}|{siteId}",
            deviceId,
            cancellationToken);
    }

    public Task UpdateLastNotifiedAsync(
        string tenantId,
        string siteId,
        string agentId,
        string deviceId,
        DateTime lastNotifiedUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = new DeviceSnapshotStateEntity
        {
            PartitionKey = $"{tenantId}|{siteId}",
            RowKey = deviceId,
            TenantId = tenantId,
            SiteId = siteId,
            AgentId = agentId,
            DeviceId = deviceId,
            LastNotifiedUtc = lastNotifiedUtc
        };

        return _store.UpsertAsync(entity, cancellationToken);
    }
}
