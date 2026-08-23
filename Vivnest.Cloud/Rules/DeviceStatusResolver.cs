using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Devices;
using Vivnest.Cloud.Options;

namespace Vivnest.Cloud.Rules;

// Shared by HealthMonitorService (drives notifications) and
// DeviceQueryService (drives the dashboard) - previously duplicated
// near-verbatim in both, which is exactly how the two could silently drift.
public sealed class DeviceStatusResolver : IDeviceStatusResolver
{
    private static readonly TimeSpan DefaultAgentStaleAfter = TimeSpan.FromMinutes(5);

    private readonly HealthMonitorOptions _options;

    public DeviceStatusResolver(IOptions<HealthMonitorOptions> options)
    {
        _options = options.Value;
    }

    public DeviceStatusResult Determine(
        DeviceHeartbeatEntity device,
        AgentHeartbeatEntity? agent,
        DeviceHeartbeatEntity? parentDevice = null)
    {
        if (agent is null)
        {
            return new DeviceStatusResult(
                DeviceHeartbeatStatus.Unknown,
                AgentCascade: false,
                HomeAssistantCascade: false,
                ParentDeviceCascade: false,
                StatusSinceUtc: null);
        }

        var agentHeartbeatInterval = TableTimeSpan.Parse(agent.HeartbeatInterval);

        var staleAfter = agentHeartbeatInterval > TimeSpan.Zero
            ? agentHeartbeatInterval * _options.AgentStaleMultiplier
            : DefaultAgentStaleAfter;

        var agentElapsed = DateTime.UtcNow - agent.LastHeartbeatUtc;

        if (agentElapsed > staleAfter)
        {
            // "Since" here is the agent's own last confirmed-alive moment,
            // not the device's - the device may have been fine right up
            // until the agent died, so the agent's last heartbeat is the
            // last point either can actually be trusted.
            return new DeviceStatusResult(
                DeviceHeartbeatStatus.Offline,
                AgentCascade: true,
                HomeAssistantCascade: false,
                ParentDeviceCascade: false,
                StatusSinceUtc: agent.LastHeartbeatUtc);
        }

        var source = Enum.TryParse<DeviceHeartbeatSource>(device.Source, out var parsedSource)
            ? parsedSource
            : DeviceHeartbeatSource.Native;

        if (source == DeviceHeartbeatSource.HomeAssistant)
        {
            // The agent process is fine, but if it's this device's HA
            // connection specifically that's stale (or never connected at
            // all), nothing the device last reported can be trusted either -
            // same reasoning as the agent cascade above, one level down.
            var haElapsed = agent.HomeAssistantLastConnectedUtc is { } lastConnectedUtc
                ? DateTime.UtcNow - lastConnectedUtc
                : (TimeSpan?)null;

            if (haElapsed is null || haElapsed > _options.HomeAssistantConnectionStaleAfter)
            {
                return new DeviceStatusResult(
                    DeviceHeartbeatStatus.Unknown,
                    AgentCascade: false,
                    HomeAssistantCascade: true,
                    ParentDeviceCascade: false,
                    StatusSinceUtc: null);
            }
        }

        // Same reasoning one level down again: this device is reached
        // through another registered device (e.g. a motion sensor through a
        // Tapo hub, DeviceOptions.ParentDeviceId) rather than the agent's
        // own HA connection - if that parent isn't reachable, nothing this
        // device last reported can be trusted either. Resolved via a
        // recursive call so the parent gets the exact same evaluation any
        // other device would (including its own Agent/HA cascade checks
        // above) - only one level deep, the recursive call passes no
        // parentDevice of its own, since nothing in the product today nests
        // hubs within hubs. No parentDevice fetched (hub hasn't reported
        // any heartbeat yet, or wasn't looked up) is treated the same as
        // "parent not Online" - can't vouch for the child without it.
        if (!string.IsNullOrWhiteSpace(device.ParentDeviceId))
        {
            var parentStatus = parentDevice is null
                ? DeviceHeartbeatStatus.Unknown
                : Determine(parentDevice, agent).Status;

            if (parentStatus != DeviceHeartbeatStatus.Healthy)
            {
                return new DeviceStatusResult(
                    DeviceHeartbeatStatus.Unknown,
                    AgentCascade: false,
                    HomeAssistantCascade: false,
                    ParentDeviceCascade: true,
                    StatusSinceUtc: null);
            }
        }

        // Agent (and, for HA-sourced devices, the HA connection) is alive
        // now, but if this device hasn't reported anything since the
        // *current agent process* started, its stored Status predates
        // that process and can't be trusted yet - a restart can lose local
        // runtime state, so the device needs to prove itself again before
        // its old Status is trusted. Deliberately anchored on
        // agent.StartedUtc, not agent.LastRecoveredUtc: LastRecoveredUtc is
        // when *Cloud* noticed the agent come back (delayed by whatever the
        // health-check cadence is), which has no causal ordering guarantee
        // against DeviceHeartbeatWorker's own first-tick publish (every
        // device publishes once immediately on process start, since
        // DeviceRuntimeState.LastReportedStatus starts null and so always
        // differs from the first computed status). Comparing against that
        // Cloud-side timestamp let a device's legitimate first-tick
        // heartbeat - timestamped at or moments after StartedUtc - land
        // *before* Cloud's later recovery detection, permanently failing
        // this check for any device whose status never changes again
        // (verified live: camera-001/motion-001 stuck on Unknown for hours
        // after a routine redeploy). StartedUtc has no such race - it's
        // always causally before any heartbeat this process can publish.
        if (device.LastHeartbeatUtc < agent.StartedUtc)
        {
            return new DeviceStatusResult(
                DeviceHeartbeatStatus.Unknown,
                AgentCascade: false,
                HomeAssistantCascade: false,
                ParentDeviceCascade: false,
                StatusSinceUtc: null);
        }

        var status = Enum.TryParse<DeviceHeartbeatStatus>(device.Status, out var parsed)
            ? parsed
            : DeviceHeartbeatStatus.Unknown;

        // Self-reported status: DeviceHeartbeatWorker/HomeAssistantLivenessTracker
        // only write a new row when the status actually changes, so
        // LastHeartbeatUtc already is "since when has this status held" -
        // no separate bookkeeping needed.
        return new DeviceStatusResult(
            status,
            AgentCascade: false,
            ParentDeviceCascade: false,
            HomeAssistantCascade: false,
            StatusSinceUtc: device.LastHeartbeatUtc);
    }
}
