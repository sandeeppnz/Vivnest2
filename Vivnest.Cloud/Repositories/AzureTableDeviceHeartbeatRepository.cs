using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

public class AzureTableDeviceHeartbeatRepository : IDeviceHeartbeatRepository
{
    private readonly AzureTableStore<DeviceHeartbeatEntity> _store;

    public AzureTableDeviceHeartbeatRepository(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<DeviceHeartbeatEntity>(
            tableServiceClient,
            tablesOptions.Value.DeviceHeartbeat);
    }

    public Task<IReadOnlyList<DeviceHeartbeatEntity>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(cancellationToken: cancellationToken);
    }

    public Task<DeviceHeartbeatEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(partitionKey, rowKey, cancellationToken);
    }

    public Task UpdateNotificationStateAsync(
        DeviceHeartbeatEntity entity,
        DeviceNotificationState notificationState,
        DateTime? lastOfflineNotificationUtc,
        DateTime? lastRecoveredUtc,
        CancellationToken cancellationToken = default)
    {
        entity.NotificationState = notificationState.ToString();
        entity.LastOfflineNotificationUtc = lastOfflineNotificationUtc;
        entity.LastRecoveredUtc = lastRecoveredUtc;

        return _store.UpdateAsync(entity, cancellationToken);
    }
}
