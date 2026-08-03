using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

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
        AgentHeartbeatEntity? agent)
    {
        if (agent is null)
        {
            return new DeviceStatusResult(
                DeviceHeartbeatStatus.Unknown,
                AgentCascade: false,
                HomeAssistantCascade: false,
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
                    StatusSinceUtc: null);
            }
        }

        // Agent (and, for HA-sourced devices, the HA connection) is alive
        // now, but if it only just recovered from a detected outage and
        // this device hasn't reported anything since that recovery, its
        // stored Status predates the outage and can't be trusted yet - the
        // agent process being back doesn't mean this specific device is.
        // Show Unknown until the device itself proves it.
        if (agent.LastRecoveredUtc is { } recoveredUtc && device.LastHeartbeatUtc < recoveredUtc)
        {
            return new DeviceStatusResult(
                DeviceHeartbeatStatus.Unknown,
                AgentCascade: false,
                HomeAssistantCascade: false,
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
            HomeAssistantCascade: false,
            StatusSinceUtc: device.LastHeartbeatUtc);
    }
}
