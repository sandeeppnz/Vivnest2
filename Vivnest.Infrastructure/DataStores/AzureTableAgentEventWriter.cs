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

    public AzureTableAgentEventWriter(
        IOptions<AgentEventOptions> agentEventOptions, IOptions<TablesOptions> tablesOptions, IOptions<StorageOptions> storageOptions)
    {
        var agentEventSettings = agentEventOptions.Value;
        var tablesSettings = tablesOptions.Value;
        var storageSettings = storageOptions.Value;

        _enabled = agentEventSettings.Enabled;

        if (!_enabled)
        {
            return;
        }

        var service = new TableServiceClient(
            storageSettings.ConnectionString);

        _table = service.GetTableClient(
            tablesSettings.AgentEvents);

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
