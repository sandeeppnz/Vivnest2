using Vivnest.Core.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Infrastructure.Stores.Heartbeats;

public static class DeviceHeartbeatMapping
{
    public static DeviceHeartbeat ToModel(
        this DeviceHeartbeatEntity entity)
    {
        return new DeviceHeartbeat
        {
            AgentId = entity.PartitionKey,
            
            TenantId = entity.TenantId,
            SiteId = entity.SiteId,

            DeviceId = entity.RowKey,

            DeviceType = Enum.Parse<DeviceType>(entity.DeviceType),
            Status = Enum.Parse<DeviceHeartbeatStatus>(entity.Status),

            LastHeartbeatUtc = entity.LastHeartbeatUtc,
            LastActivityUtc = entity.LastActivityUtc,

            ExpectedActivityInterval = entity.ExpectedActivityInterval,
            ExpectedHeartbeatInterval = entity.ExpectedHeartbeatInterval,
            Error = entity.Error
        };
    }
}
