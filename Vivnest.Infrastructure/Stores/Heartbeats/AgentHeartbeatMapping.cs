using Vivnest.Core.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Infrastructure.Stores.Heartbeats;

public static class AgentHeartbeatMapping
{
    public static AgentHeartbeat ToModel(
        this AgentHeartbeatEntity entity)
    {
        return new AgentHeartbeat
        {
            AgentId = entity.PartitionKey,
            FirmwareVersion = entity.FirmwareVersion,
            HostName = entity.HostName,
            StartedUtc = entity.StartedUtc,
            LastHeartbeatUtc = entity.LastHeartbeatUtc,
            Error = entity.Error,
            HeartbeatInterval = entity.HeartbeatInterval
        };
    }
}
