using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

public sealed class DeviceHeartbeatEntity : AgentEntity, ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string DeviceType { get; set; } = default!;

    public string Status { get; set; } = default!;

    public DateTime LastHeartbeatUtc { get; set; }

    public DateTime? LastActivityUtc { get; set; }

    public string? AgentFirmwareVersion { get; set; }

    public string? Error { get; set; }

    // Both stored as TimeSpan.ToString(), not TimeSpan - see TableTimeSpan.
    public string ExpectedLivenessInterval { get; set; } = default!;
    public string ExpectedHeartbeatInterval { get; init; } = default!;

    public string NotificationState { get; set; } = default!;
    public DateTime? LastOfflineNotificationUtc { get; set; }
    public DateTime? LastRecoveredUtc { get; set; }
}
