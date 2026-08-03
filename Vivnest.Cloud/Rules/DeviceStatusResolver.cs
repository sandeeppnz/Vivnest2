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
            return new DeviceStatusResult(DeviceHeartbeatStatus.Unknown, AgentCascade: false);

        var agentHeartbeatInterval = TableTimeSpan.Parse(agent.HeartbeatInterval);

        var staleAfter = agentHeartbeatInterval > TimeSpan.Zero
            ? agentHeartbeatInterval * _options.AgentStaleMultiplier
            : DefaultAgentStaleAfter;

        var agentElapsed = DateTime.UtcNow - agent.LastHeartbeatUtc;

        if (agentElapsed > staleAfter)
            return new DeviceStatusResult(DeviceHeartbeatStatus.Offline, AgentCascade: true);

        // Agent is alive now, but if it only just recovered from a detected
        // outage and this device hasn't reported anything since that
        // recovery, its stored Status predates the outage and can't be
        // trusted yet - the agent process being back doesn't mean this
        // specific device is. Show Unknown until the device itself proves it.
        if (agent.LastRecoveredUtc is { } recoveredUtc && device.LastHeartbeatUtc < recoveredUtc)
            return new DeviceStatusResult(DeviceHeartbeatStatus.Unknown, AgentCascade: false);

        var status = Enum.TryParse<DeviceHeartbeatStatus>(device.Status, out var parsed)
            ? parsed
            : DeviceHeartbeatStatus.Unknown;

        return new DeviceStatusResult(status, AgentCascade: false);
    }
}
