using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentEventReader
{
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
