using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Domain.Devices;

namespace Vivnest.Cloud.Rules;

public sealed class AgentStatusResolver : IAgentStatusResolver
{
    private static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromMinutes(5);

    private readonly HealthMonitorOptions _options;

    public AgentStatusResolver(IOptions<HealthMonitorOptions> options)
    {
        _options = options.Value;
    }

    public AgentStatusResult Determine(AgentHeartbeatEntity? agent)
    {
        if (agent is null)
            return new AgentStatusResult(DeviceHeartbeatStatus.Unknown, null);

        var heartbeatInterval = TableTimeSpan.Parse(agent.HeartbeatInterval);

        var baseInterval = heartbeatInterval > TimeSpan.Zero
            ? heartbeatInterval
            : DefaultStaleAfter;

        var elapsed = DateTime.UtcNow - agent.LastHeartbeatUtc;

        if (elapsed <= baseInterval * _options.AgentDegradedMultiplier)
        {
            // Healthy since its last recorded recovery, or - if it's never
            // actually been marked offline (LastRecoveredUtc never set) -
            // since this process started. Same reasoning
            // AgentQueryService.ToDto used to compute by hand.
            return new AgentStatusResult(
                DeviceHeartbeatStatus.Healthy,
                agent.LastRecoveredUtc ?? agent.StartedUtc);
        }

        if (elapsed <= baseInterval * _options.AgentOfflineMultiplier)
        {
            return new AgentStatusResult(
                DeviceHeartbeatStatus.Degraded,
                agent.LastHeartbeatUtc);
        }

        return new AgentStatusResult(
            DeviceHeartbeatStatus.Offline,
            agent.LastHeartbeatUtc);
    }
}
