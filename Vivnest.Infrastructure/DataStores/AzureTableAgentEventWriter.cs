using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Domain.Agents;

namespace Vivnest.Infrastructure.DataStores;

public sealed class AzureTableAgentEventWriter : IAgentEventWriter
{
    private readonly TableClient? _table;
    private readonly bool _enabled;

    // Takes the DI-registered TableServiceClient, which AddInfrastructure
    // creates once from Storage:ConnectionString - this class used to new
    // up a second client from the same connection string, making it one of
    // two places that read the setting and the only writer that bypassed
    // the shared client the heartbeat writers already inject.
    public AzureTableAgentEventWriter(
        IOptions<AgentEventOptions> agentEventOptions, IOptions<TablesOptions> tablesOptions, TableServiceClient tableServiceClient)
    {
        _enabled = agentEventOptions.Value.Enabled;

        if (!_enabled)
        {
            return;
        }

        _table = tableServiceClient.GetTableClient(
            tablesOptions.Value.AgentEvents);

        _table.CreateIfNotExists();
    }

    public async Task<AgentEventEntity?> SaveAsync(
        AgentEvent agentEvent,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return null;
        }

        var entity = new AgentEventEntity
        {
            PartitionKey = agentEvent.AgentId,
            RowKey = EventRowKey.For(agentEvent.OccurredAtUtc, agentEvent.EventId),
            AgentId = agentEvent.AgentId,
            TenantId = agentEvent.TenantId,
            SiteId = agentEvent.SiteId,
            EventType = agentEvent.EventType,
            Severity = agentEvent.Severity.ToString(),
            OccurredAtUtc = agentEvent.OccurredAtUtc,
            Payload = JsonSerializer.Serialize(agentEvent.Data)
        };

        await _table!.AddEntityAsync(
            entity,
            cancellationToken);

        return entity;
    }
}
