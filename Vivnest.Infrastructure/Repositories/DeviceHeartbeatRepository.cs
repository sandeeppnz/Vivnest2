using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Interfaces.Heartbeats;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options.Heartbeats;

namespace Vivnest.Infrastructure.Repositories;

public sealed class DeviceHeartbeatRepository : IDeviceHeartbeatRepository
{
    private readonly TableClient _table;

    public DeviceHeartbeatRepository(
        IOptions<DeviceHeartbeatOptions> options)
    {
        var settings = options.Value;

        _table = new TableClient(
            settings.ConnectionString,
            settings.TableName);

        _table.CreateIfNotExists();
    }

    public async Task UpsertAsync(
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
            var response = await _table.GetEntityAsync<DeviceHeartbeatEntity>(
                partitionKey: $"{tenantId}|{siteId}|{agentId}",
                rowKey: deviceId,
                cancellationToken: cancellationToken);

            var entity = response.Value;

            return ToModel(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IEnumerable<DeviceHeartbeat>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DeviceHeartbeat>();

        await foreach (var entity in _table.QueryAsync<DeviceHeartbeatEntity>(
                           x => x.PartitionKey == $"{tenantId}|{siteId}|{agentId}",
                           cancellationToken: cancellationToken))
        {
            results.Add(ToModel(entity));
        }

        return results;
    }

    private static DeviceHeartbeat ToModel(DeviceHeartbeatEntity entity)
    {
        return new DeviceHeartbeat
        {
            TenantId = entity.TenantId,
            SiteId = entity.SiteId,
            AgentId = entity.AgentId,
            DeviceId = entity.RowKey,
            DeviceType = Enum.Parse<DeviceType>(entity.DeviceType),
            Status = Enum.Parse<DeviceHeartbeatStatus>(entity.Status),
            LastHeartbeatUtc = entity.LastHeartbeatUtc,
            LastActivityUtc = entity.LastActivityUtc,
            ExpectedActivityInterval = entity.ExpectedActivityInterval,
            Error = entity.Error
        };
    }
}
