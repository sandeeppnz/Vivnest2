using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

public sealed class DeviceHeartbeatEntity : AgentEntity, ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string? Name { get; set; }

    public string DeviceType { get; set; } = default!;

    public string Status { get; set; } = default!;

    public DateTime LastHeartbeatUtc { get; set; }

    public DateTime? LastActivityUtc { get; set; }

    public string? AgentFirmwareVersion { get; set; }

    public string? Error { get; set; }

    // Both stored as TimeSpan.ToString(), not TimeSpan - see TableTimeSpan.
    public string ExpectedLivenessInterval { get; set; } = default!;
    public string ExpectedHeartbeatInterval { get; set; } = default!;

    public string NotificationState { get; set; } = default!;
    public DateTime? LastOfflineNotificationUtc { get; set; }
    public DateTime? LastRecoveredUtc { get; set; }

    // "Native" for rows predating this field (Enum.TryParse fails on empty
    // string) - a conservative default, since treating a genuinely-native
    // device as HomeAssistant-sourced would wrongly subject it to the HA
    // connection cascade.
    public string Source { get; set; } = default!;

    // The DeviceId this device is reached through, if any - see
    // DeviceOptions.ParentDeviceId. Empty/null for devices with no parent.
    public string? ParentDeviceId { get; set; }

    // The owning agent's IANA timezone, denormalized at heartbeat time -
    // see DeviceHeartbeat.Timezone. Null for rows predating this field;
    // readers treat null/empty as UTC.
    public string? Timezone { get; set; }

    // Descriptive-only - see DeviceHeartbeat.Location/Brand/Model/Firmware.
    public string? Location { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Firmware { get; set; }

    // See DeviceHeartbeat.SinkCleanlinessEnabled/ObjectDetectionEnabled.
    public bool SinkCleanlinessEnabled { get; set; }
    public bool ObjectDetectionEnabled { get; set; }

    // See DeviceHeartbeat.ConfigurationPublishedUtc (decision-log.md ADR-065).
    public DateTime? ConfigurationPublishedUtc { get; set; }

    // See DeviceHeartbeat.ConfigurationVersion/ConfigurationHash
    // (decision-log.md ADR-069).
    public int? ConfigurationVersion { get; set; }
    public string? ConfigurationHash { get; set; }
}
