using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

public sealed class AgentHeartbeatEntity : AgentEntity, ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime LastHeartbeatUtc { get; set; }

    public string HostName { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string FirmwareVersion { get; set; } = default!;

    public string RuntimeVersion { get; set; } = default!;

    public string OsDescription { get; set; } = default!;

    public string? Error { get; set; }

    // Stored as TimeSpan.ToString(), not TimeSpan - see TableTimeSpan.
    public string HeartbeatInterval { get; set; } = default!;

    public string NotificationState { get; set; } = default!;
    public DateTime? LastOfflineNotificationUtc { get; set; }
    public DateTime? LastRecoveredUtc { get; set; }

    public DateTime? HomeAssistantLastConnectedUtc { get; set; }

    // See AgentHeartbeat.ConfigurationPublishedUtc (decision-log.md ADR-065).
    public DateTime? ConfigurationPublishedUtc { get; set; }
}
