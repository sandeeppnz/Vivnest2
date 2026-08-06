using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Storage;

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
            Name = entity.Name ?? string.Empty,

            DeviceType = Enum.Parse<DeviceType>(entity.DeviceType),
            Status = Enum.Parse<DeviceHeartbeatStatus>(entity.Status),

            LastHeartbeatUtc = entity.LastHeartbeatUtc,
            LastActivityUtc = entity.LastActivityUtc,

            ExpectedLivenessInterval = TableTimeSpan.Parse(entity.ExpectedLivenessInterval),
            ExpectedHeartbeatInterval = TableTimeSpan.Parse(entity.ExpectedHeartbeatInterval),
            Source = Enum.TryParse<DeviceHeartbeatSource>(entity.Source, out var source)
                ? source
                : DeviceHeartbeatSource.Native,
            Error = entity.Error,
            ParentDeviceId = entity.ParentDeviceId,
            Timezone = entity.Timezone ?? string.Empty,
            Location = entity.Location ?? string.Empty,
            Brand = entity.Brand ?? string.Empty,
            Model = entity.Model ?? string.Empty,
            Firmware = entity.Firmware ?? string.Empty,

            LastOfflineNotificationUtc = entity.LastOfflineNotificationUtc,
            LastRecoveredUtc = entity.LastRecoveredUtc,
            NotificationState = Enum.Parse<DeviceNotificationState>(entity.NotificationState)
        };
    }
}
