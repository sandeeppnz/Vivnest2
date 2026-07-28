using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.Entities;
using Vivnest.Core.Interfaces.Stores;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options;
using Vivnest.Core.Options.Heartbeats;

namespace Vivnest.Infrastructure.Stores.Heartbeats;

public sealed class AgentHeartbeatStore : IAgentHeartbeatStore
{
    private readonly TableClient _table;

    public AgentHeartbeatStore(TableServiceClient tableServiceClient, IOptions<TablesOptions> tablesOptions)
    {
        var tablesSettings = tablesOptions.Value;

        _table = tableServiceClient.GetTableClient(tablesSettings.AgentHeartbeat);
        _table.CreateIfNotExists();
    }

    public async Task SaveAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        var entity = new AgentHeartbeatEntity
        {
            PartitionKey = $"{heartbeat.TenantId}|{heartbeat.SiteId}",
            RowKey = heartbeat.AgentId,
            HostName = heartbeat.HostName,
            StartedUtc = heartbeat.StartedUtc,
            LastHeartbeatUtc = heartbeat.LastHeartbeatUtc,
            Error = heartbeat.Error,
            HeartbeatInterval = heartbeat.HeartbeatInterval,
            AgentId = heartbeat.AgentId,
            TenantId = heartbeat.TenantId,
            SiteId = heartbeat.SiteId
        };

        await _table.UpsertEntityAsync(
            entity,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    public async Task<AgentHeartbeat?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity =
                await _table.GetEntityAsync<AgentHeartbeatEntity>(
                    $"{tenantId}|{siteId}",
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
