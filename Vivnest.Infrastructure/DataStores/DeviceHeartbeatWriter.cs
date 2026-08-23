using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.DataStores.Helpers;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Sites;

namespace Vivnest.Infrastructure.DataStores;

public sealed class DeviceHeartbeatWriter : IDeviceHeartbeatWriter
{
    private readonly AzureTableStore<DeviceHeartbeatEntity> _store;

    public DeviceHeartbeatWriter(TableServiceClient tableServiceClient, IOptions<TablesOptions> options)
    {
        _store = new AzureTableStore<DeviceHeartbeatEntity>(
            tableServiceClient,
            options.Value.DeviceHeartbeat);
    }

    public async Task<DeviceHeartbeatEntity> SaveAsync(
        DeviceHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = $"{new SiteScope(heartbeat.TenantId, heartbeat.SiteId).PartitionKey}|{heartbeat.AgentId}";
        var rowKey = heartbeat.DeviceId;

        // Same reasoning as AgentHeartbeatWriter: UpsertAsync replaces the
        // whole row, and the agent-side domain model has no concept of
        // Cloud-owned NotificationState/LastOfflineNotificationUtc/
        // LastRecoveredUtc - read the existing row first so a status-change
        // write doesn't silently wipe them.
        var existing = await _store.GetAsync(partitionKey, rowKey, cancellationToken);

        var entity = new DeviceHeartbeatEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            AgentId = heartbeat.AgentId,
            TenantId = heartbeat.TenantId,
            SiteId = heartbeat.SiteId,

            Name = heartbeat.Name,
            DeviceType = heartbeat.DeviceType.ToString(),
            Status = heartbeat.Status.ToString(),
            Source = heartbeat.Source.ToString(),

            LastHeartbeatUtc = heartbeat.LastHeartbeatUtc,
            LastActivityUtc = heartbeat.LastActivityUtc,
            ExpectedLivenessInterval = TableTimeSpan.ToStorageString(heartbeat.ExpectedLivenessInterval),
            ExpectedHeartbeatInterval = TableTimeSpan.ToStorageString(heartbeat.ExpectedHeartbeatInterval),

            Error = heartbeat.Error,
            ParentDeviceId = heartbeat.ParentDeviceId,
            Timezone = heartbeat.Timezone,
            Location = heartbeat.Location,
            Brand = heartbeat.Brand,
            Model = heartbeat.Model,
            Firmware = heartbeat.Firmware,
            SinkCleanlinessEnabled = heartbeat.SinkCleanlinessEnabled,
            ObjectDetectionEnabled = heartbeat.ObjectDetectionEnabled,
            ConfigurationPublishedUtc = heartbeat.ConfigurationPublishedUtc,
            ConfigurationVersion = heartbeat.ConfigurationVersion,
            ConfigurationHash = heartbeat.ConfigurationHash,

            LastOfflineNotificationUtc = existing?.LastOfflineNotificationUtc,
            LastRecoveredUtc = existing?.LastRecoveredUtc,
            NotificationState = existing?.NotificationState ?? DeviceNotificationState.None.ToString()
        };

        return await _store.UpsertAsync(entity, cancellationToken);
    }

    public async Task<DeviceHeartbeat?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _store.GetAsync(
            $"{new SiteScope(tenantId, siteId).PartitionKey}|{agentId}",
            deviceId,
            cancellationToken);

        return entity?.ToModel();
    }

    public async Task<IReadOnlyList<DeviceHeartbeat>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = $"{new SiteScope(tenantId, siteId).PartitionKey}|{agentId}";

        var entities = await _store.QueryAsync(
            x => x.PartitionKey == partitionKey,
            cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }
}
