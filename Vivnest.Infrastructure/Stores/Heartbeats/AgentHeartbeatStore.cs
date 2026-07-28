using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.Entities;
using Vivnest.Core.Interfaces.Stores;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options.Heartbeats;

namespace Vivnest.Infrastructure.Stores.Heartbeats;

public sealed class AgentHeartbeatStore : IAgentHeartbeatStore
{
    private readonly TableClient _table;

    public AgentHeartbeatStore(
        TableServiceClient tableServiceClient,
        IOptions<AgentHeartbeatOptions> options)
    {
        _table = tableServiceClient.GetTableClient(options.Value.TableName);
        _table.CreateIfNotExists();
    }

    public async Task SaveAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        var entity = new AgentHeartbeatEntity
        {
            PartitionKey = heartbeat.AgentId,
            RowKey = heartbeat.AgentId,

            Status = heartbeat.Status.ToString(),
            FirmwareVersion = heartbeat.FirmwareVersion,
            HostName = heartbeat.HostName,
            StartedUtc = heartbeat.StartedUtc,
            LastHeartbeatUtc = heartbeat.LastHeartbeatUtc,
            Error = heartbeat.Error,
            HeartbeatInterval = heartbeat.HeartbeatInterval
        };

        await _table.UpsertEntityAsync(
            entity,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    public async Task<AgentHeartbeat?> GetAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity =
                await _table.GetEntityAsync<AgentHeartbeatEntity>(
                    agentId,
                    agentId,
                    cancellationToken: cancellationToken);

            return entity.Value.ToModel();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
