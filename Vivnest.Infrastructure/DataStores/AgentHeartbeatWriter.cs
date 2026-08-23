using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.DataStores.Helpers;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Sites;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Infrastructure.DataStores;

public sealed class AgentHeartbeatWriter : IAgentHeartbeatWriter
{
    private readonly AzureTableStore<AgentHeartbeatEntity> _store;

    public AgentHeartbeatWriter(TableServiceClient tableServiceClient, IOptions<TablesOptions> tablesOptions)
    {
        _store = new AzureTableStore<AgentHeartbeatEntity>(
            tableServiceClient,
            tablesOptions.Value.AgentHeartbeat);
    }

    public async Task<AgentHeartbeatEntity> SaveAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(heartbeat.TenantId, heartbeat.SiteId).PartitionKey;
        var rowKey = heartbeat.AgentId;

        // AzureTableStore.UpsertAsync replaces the whole row - the agent's
        // own domain model has no concept of NotificationState/
        // LastOfflineNotificationUtc/LastRecoveredUtc (Cloud-only fields),
        // so without reading the existing row first, every heartbeat tick
        // would silently wipe whatever Cloud just set.
        var existing = await _store.GetAsync(partitionKey, rowKey, cancellationToken);

        var entity = new AgentHeartbeatEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            HostName = heartbeat.HostName,
            Name = heartbeat.Name,
            FirmwareVersion = heartbeat.FirmwareVersion,
            RuntimeVersion = heartbeat.RuntimeVersion,
            OsDescription = heartbeat.OsDescription,
            StartedUtc = heartbeat.StartedUtc,
            LastHeartbeatUtc = heartbeat.LastHeartbeatUtc,
            Error = heartbeat.Error,
            HeartbeatInterval = TableTimeSpan.ToStorageString(heartbeat.HeartbeatInterval),
            AgentId = heartbeat.AgentId,
            TenantId = heartbeat.TenantId,
            SiteId = heartbeat.SiteId,
            HomeAssistantLastConnectedUtc = heartbeat.HomeAssistantLastConnectedUtc,
            ConfigurationPublishedUtc = heartbeat.ConfigurationPublishedUtc,
            ConfigurationLoadError = heartbeat.ConfigurationLoadError,
            ConfigurationVersion = heartbeat.ConfigurationVersion,
            ConfigurationHash = heartbeat.ConfigurationHash,

            NotificationState = existing?.NotificationState ?? DeviceNotificationState.None.ToString(),
            LastOfflineNotificationUtc = existing?.LastOfflineNotificationUtc,
            LastRecoveredUtc = existing?.LastRecoveredUtc
        };

        return await _store.UpsertAsync(entity, cancellationToken);
    }

    public async Task<AgentHeartbeat?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _store.GetAsync(
            new SiteScope(tenantId, siteId).PartitionKey,
            agentId,
            cancellationToken);

        return entity?.ToModel();
    }
}
