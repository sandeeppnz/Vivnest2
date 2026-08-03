using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;

namespace Vivnest.Cloud.Repositories;

public class AzureTableAgentEventReader : IAgentEventReader
{
    private readonly TableClient? _table;
    private readonly bool _enabled;

    public AzureTableAgentEventReader(
        IOptions<AgentEventOptions> options,
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _enabled = options.Value.Enabled;

        if (!_enabled)
        {
            return;
        }

        var tableSettings = tablesOptions.Value;
        _table = tableServiceClient.GetTableClient(tableSettings.AgentEvents);

        _table.CreateIfNotExists();
    }

    public async Task<IReadOnlyList<AgentEventEntity>> GetByAgentAndDateRangeAsync(
        string tenantId,
        string siteId,
        string agentId,
        string? eventType,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return Array.Empty<AgentEventEntity>();

        // RowKey is "{OccurredAtUtc:yyyyMMddHHmmssfff}-{EventId}", so it's
        // already sorted by time within the partition - a RowKey range
        // filter narrows the query at the table service instead of
        // fetching every row for this agent and filtering client-side.
        var fromRowKey = fromUtc.ToString("yyyyMMddHHmmssfff");
        var toRowKey = toUtc.ToString("yyyyMMddHHmmssfff");

        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {agentId} and RowKey ge {fromRowKey} and RowKey lt {toRowKey}");

        var query = _table!.QueryAsync<AgentEventEntity>(
            filter,
            cancellationToken: cancellationToken);

        var results = new List<AgentEventEntity>();

        await foreach (var entity in query)
        {
            if (entity.TenantId != tenantId || entity.SiteId != siteId)
                continue;

            if (eventType != null && entity.EventType != eventType)
                continue;

            results.Add(entity);
        }

        return results
            .OrderBy(e => e.OccurredAtUtc)
            .ToList();
    }

    public async Task<int> DeleteOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return 0;

        var query = _table!.QueryAsync<AgentEventEntity>(
            e => e.OccurredAtUtc < cutoffUtc,
            cancellationToken: cancellationToken);

        var deleted = 0;

        await foreach (var entity in query)
        {
            await _table.DeleteEntityAsync(
                entity.PartitionKey,
                entity.RowKey,
                cancellationToken: cancellationToken);

            deleted++;
        }

        return deleted;
    }
}
