using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Infrastructure.DataStores.Helpers;

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

            ExpectedLivenessInterval = entity.ExpectedLivenessInterval,
            ExpectedHeartbeatInterval = entity.ExpectedHeartbeatInterval,
            Error = entity.Error,

            LastOfflineNotificationUtc = entity.LastOfflineNotificationUtc,
            LastRecoveredUtc = entity.LastRecoveredUtc,
            NotificationState = Enum.Parse<DeviceNotificationState>(entity.NotificationState)
        };
    }
}
