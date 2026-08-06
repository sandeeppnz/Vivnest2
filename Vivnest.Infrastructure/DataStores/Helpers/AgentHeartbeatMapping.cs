using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Storage;

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
            Name = entity.Name,
            FirmwareVersion = entity.FirmwareVersion,
            RuntimeVersion = entity.RuntimeVersion,
            OsDescription = entity.OsDescription,
            StartedUtc = entity.StartedUtc,
            LastHeartbeatUtc = entity.LastHeartbeatUtc,
            Error = entity.Error,
            HeartbeatInterval = TableTimeSpan.Parse(entity.HeartbeatInterval),
            HomeAssistantLastConnectedUtc = entity.HomeAssistantLastConnectedUtc
        };
    }
}
