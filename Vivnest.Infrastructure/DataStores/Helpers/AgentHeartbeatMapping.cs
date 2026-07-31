using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;

namespace Vivnest.Infrastructure.DataStores.Helpers;

public static class AgentHeartbeatMapping
{
    public static AgentHeartbeat ToModel(
        this AgentHeartbeatEntity entity)
    {
        return new AgentHeartbeat
        {
            AgentId = entity.AgentId,
            TenantId = entity.TenantId,
            SiteId = entity.SiteId,
            HostName = entity.HostName,
            StartedUtc = entity.StartedUtc,
            LastHeartbeatUtc = entity.LastHeartbeatUtc,
            Error = entity.Error,
            HeartbeatInterval = entity.HeartbeatInterval
        };
    }
}
