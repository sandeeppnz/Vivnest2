using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.Entities;
using Vivnest.Core.Interfaces.Stores;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options.Heartbeats;

namespace Vivnest.Infrastructure.Stores.Heartbeats;

public sealed class DeviceHeartbeatStore : IDeviceHeartbeatStore
{
    private readonly TableClient _table;

    public DeviceHeartbeatStore(
        TableServiceClient tableServiceClient,
        IOptions<DeviceHeartbeatOptions> options)
    {
        _table = tableServiceClient.GetTableClient(options.Value.TableName);
        _table.CreateIfNotExists();
    }

    public async Task SaveAsync(
        DeviceHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        var entity = new DeviceHeartbeatEntity
        {
            PartitionKey = heartbeat.AgentId,
            RowKey = heartbeat.DeviceId,

            DeviceType = heartbeat.DeviceType.ToString(),
            Status = heartbeat.Status.ToString(),

            LastHeartbeatUtc = heartbeat.LastHeartbeatUtc,
            LastActivityUtc = heartbeat.LastActivityUtc,
            ExpectedActivityInterval = heartbeat.ExpectedActivityInterval,

            AgentFirmwareVersion = heartbeat.AgentFirmwareVersion,
            Error = heartbeat.Error
        };

        await _table.UpsertEntityAsync(
            entity,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    public async Task<DeviceHeartbeat?> GetAsync(
        string agentId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity =
                await _table.GetEntityAsync<DeviceHeartbeatEntity>(
                    agentId,
                    deviceId,
                    cancellationToken: cancellationToken);

            return entity.Value.ToModel();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DeviceHeartbeat>> GetByAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var list = new List<DeviceHeartbeat>();

        await foreach (var entity in _table.QueryAsync<DeviceHeartbeatEntity>(
                           x => x.PartitionKey == agentId,
                           cancellationToken: cancellationToken))
        {
            list.Add(entity.ToModel());
        }

        return list;
    }
}