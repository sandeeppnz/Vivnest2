using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Devices;

namespace Vivnest.Cloud.Interfaces;

// Decision-log.md ADR-074. Reuses DeviceHeartbeatStatus rather than a new
// Agent-specific enum - Online/Warning/Offline/Unknown is exactly the
// tiered vocabulary this needs, and it's already wired into every
// dashboard status badge/filter/CSS class. StatusSinceUtc is null only
// when there's no heartbeat row at all (Unknown) - since when an agent
// has been Online is its last recovery (or process start); since when
// it's been Warning/Offline is its own last confirmed-alive heartbeat.
public readonly record struct AgentStatusResult(
    DeviceHeartbeatStatus Status,
    DateTime? StatusSinceUtc);

// Shared by HealthMonitorService (drives the offline/recovery
// notification) and AgentQueryService (drives the dashboard) - same
// "can't silently drift apart" reasoning IDeviceStatusResolver's own
// comment already states, since this codebase already hit that exact
// duplication once (AgentQueryService.ToDto used to carry its own
// hand-mirrored copy of HealthMonitorService's threshold check).
public interface IAgentStatusResolver
{
    AgentStatusResult Determine(AgentHeartbeatEntity? agent);
}
