using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

// tblAgentConfiguration and tblDeviceConfiguration were the only two tables
// in this codebase reached by constructing AzureTableStore<T> inline inside
// a service, rather than through an I*Store repository like every other
// table. That made the publishers untestable and was already recorded as an
// inconsistency in VIVNEST-DEAD-LEGACY-CODE.md; these interfaces close both.
//
// Only the three operations the publishers actually perform are exposed.
// Update carries the caller's ETag, which is what makes the concurrent-
// publish guard work - a 412 is meaningful, not an error to swallow.
public interface IAgentConfigurationStore
{
    Task<AgentConfigurationEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        AgentConfigurationEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        AgentConfigurationEntity entity,
        CancellationToken cancellationToken = default);
}

public interface IDeviceConfigurationStore
{
    Task<DeviceConfigurationEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        DeviceConfigurationEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        DeviceConfigurationEntity entity,
        CancellationToken cancellationToken = default);
}
