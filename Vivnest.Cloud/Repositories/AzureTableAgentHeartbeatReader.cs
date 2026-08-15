using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

public class AzureTableAgentHeartbeatReader : IAgentHeartbeatReader
{
    private readonly AzureTableStore<AgentHeartbeatEntity> _store;

    public AzureTableAgentHeartbeatReader(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<AgentHeartbeatEntity>(
            tableServiceClient,
            tablesOptions.Value.AgentHeartbeat);
    }

    public Task<IReadOnlyList<AgentHeartbeatEntity>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(cancellationToken: cancellationToken);
    }

    public Task<AgentHeartbeatEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(partitionKey, rowKey, cancellationToken);
    }

    public Task<IReadOnlyList<AgentHeartbeatEntity>> GetByTenantAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(
            x => x.TenantId == tenantId && x.SiteId == siteId,
            cancellationToken);
    }

    public Task UpdateNotificationStateAsync(
        AgentHeartbeatEntity entity,
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

    public Task UpdateLastNotifiedConfigurationLoadErrorAsync(
        AgentHeartbeatEntity entity,
        string? lastNotifiedConfigurationLoadError,
        CancellationToken cancellationToken = default)
    {
        entity.LastNotifiedConfigurationLoadError = lastNotifiedConfigurationLoadError;

        return _store.UpdateAsync(entity, cancellationToken);
    }
}
