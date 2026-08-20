using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentEventReader
{
    // Sprint 8 - the point read the agent-events queue consumer needs, to
    // refetch what a {PartitionKey, RowKey} message points at. Mirrors
    // IDeviceEventReader.GetAsync.
    Task<AgentEventEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentEventEntity>> GetByAgentAndDateRangeAsync(
        string tenantId,
        string siteId,
        string agentId,
        string? eventType,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // Full-table scan, filtered on the business timestamp (OccurredAtUtc) -
    // mirrors IDeviceEventReader.DeleteOlderThanAsync exactly, same
    // reasoning (fine at this data volume).
    Task<int> DeleteOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default);
}
