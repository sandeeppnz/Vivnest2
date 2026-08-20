using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

// Cloud writes AgentEvent/DeviceEvent rows of its own - config publish and
// rollback audit entries, and the offline/recovery transitions
// HealthMonitorService records. It cannot use the Agent-side
// IAgentEventWriter/IDeviceEventWriter for this, because Vivnest.Cloud has
// no reference to Vivnest.Infrastructure (recorded as U-D8 in
// the 2026-08 dead-code audit), so it constructed AzureTableStore<T>
// inline instead.
//
// That inline construction is what made the publishers untestable:
// AzureTableStore<T>'s constructor calls CreateIfNotExists(), so merely
// building a publisher reached the network. Only Upsert is exposed, since
// that is all these callers do.
public interface IAgentEventStore
{
    Task UpsertAsync(AgentEventEntity entity, CancellationToken cancellationToken = default);
}

public interface IDeviceEventStore
{
    Task UpsertAsync(DeviceEventEntity entity, CancellationToken cancellationToken = default);
}
