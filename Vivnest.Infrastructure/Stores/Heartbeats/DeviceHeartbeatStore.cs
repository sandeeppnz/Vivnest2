using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.Entities;
using Vivnest.Core.Interfaces.Stores;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options;
using Vivnest.Core.Options.Heartbeats;

namespace Vivnest.Infrastructure.Stores.Heartbeats;

public sealed class DeviceHeartbeatStore : IDeviceHeartbeatStore
{
    private readonly TableClient _table;

    public DeviceHeartbeatStore(TableServiceClient tableServiceClient, IOptions<TablesOptions> options)
    {
        var tablesSettings = options.Value;
        _table = tableServiceClient.GetTableClient(tablesSettings.DeviceHeartbeat);
        _table.CreateIfNotExists();
    }

    public async Task SaveAsync(
        DeviceHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        var entity = new DeviceHeartbeatEntity
        {
            PartitionKey = $"{heartbeat.TenantId}|{heartbeat.SiteId}|{heartbeat.AgentId}",
            RowKey = heartbeat.DeviceId,
            AgentId = heartbeat.AgentId,
            TenantId = heartbeat.TenantId,
            SiteId = heartbeat.SiteId,

            DeviceType = heartbeat.DeviceType.ToString(),
            Status = heartbeat.Status.ToString(),

            LastHeartbeatUtc = heartbeat.LastHeartbeatUtc,
            LastActivityUtc = heartbeat.LastActivityUtc,
            ExpectedActivityInterval = heartbeat.ExpectedActivityInterval,
            ExpectedHeartbeatInterval = heartbeat.ExpectedHeartbeatInterval,

            Error = heartbeat.Error
        };

        await _table.UpsertEntityAsync(
            entity,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    public async Task<DeviceHeartbeat?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity =
                await _table.GetEntityAsync<DeviceHeartbeatEntity>(
                    partitionKey: $"{tenantId}|{siteId}|{agentId}",
                    rowKey: deviceId,
                    cancellationToken: cancellationToken);

            return entity.Value.ToModel();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DeviceHeartbeat>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var list = new List<DeviceHeartbeat>();

        await foreach (var entity in _table.QueryAsync<DeviceHeartbeatEntity>(
                           x => x.PartitionKey == $"{tenantId}|{siteId}|{agentId}",
                           cancellationToken: cancellationToken))
        {
            list.Add(entity.ToModel());
        }

        return list;
    }
}