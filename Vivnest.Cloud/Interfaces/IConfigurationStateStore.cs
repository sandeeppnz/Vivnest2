using Azure.Data.Tables;
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
//
// Generic because the shared publish pipeline
// (Vivnest.Cloud.Admin.RuntimeConfigurationWriter) is written once over
// both tables. The two named interfaces below are kept purely so call
// sites that only ever mean one of them - CommandDispatcher, the DI
// registrations - still read as the specific thing they are.
public interface IConfigurationStateStore<TEntity>
    where TEntity : class, ITableEntity, IConfigurationStateEntity
{
    Task<TEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);
}

public interface IAgentConfigurationStore : IConfigurationStateStore<AgentConfigurationEntity>;

public interface IDeviceConfigurationStore : IConfigurationStateStore<DeviceConfigurationEntity>;
