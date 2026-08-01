using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.DataStores.Helpers;

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

    public Task<AgentHeartbeatEntity> SaveAsync(
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
            HeartbeatInterval = TableTimeSpan.ToStorageString(heartbeat.HeartbeatInterval),
            AgentId = heartbeat.AgentId,
            TenantId = heartbeat.TenantId,
            SiteId = heartbeat.SiteId
        };

        return _store.UpsertAsync(entity, cancellationToken);
    }

    public async Task<AgentHeartbeat?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _store.GetAsync(
            $"{tenantId}|{siteId}",
            agentId,
            cancellationToken);

        return entity?.ToModel();
    }
}
