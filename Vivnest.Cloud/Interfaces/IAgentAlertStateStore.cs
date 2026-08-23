using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

// Sprint 8 - only the two operations the throttle performs. Upsert rather
// than Update: a first-ever signature has no row, and losing a race here
// costs at worst one duplicate notification, which is not worth an ETag
// retry loop.
public interface IAgentAlertStateStore
{
    Task<AgentAlertStateEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        AgentAlertStateEntity entity,
        CancellationToken cancellationToken = default);
}
