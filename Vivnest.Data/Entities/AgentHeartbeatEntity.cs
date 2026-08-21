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

    // See AgentHeartbeat.ConfigurationLoadError (decision-log.md ADR-068).
    public string? ConfigurationLoadError { get; set; }

    // Decision-log.md ADR-077 - mirrors NotificationState's own "fire once
    // per transition, not every health-check tick" reasoning, but for
    // ConfigurationApplyFailed specifically (a distinct concept from
    // Online/Offline, so it needs its own gate rather than overloading
    // NotificationState). Null means "no failure notified yet" (or the
    // last one was resolved and cleared); a non-null value is the exact
    // error string already notified, so a *changed* error re-notifies but
    // an unchanged one doesn't spam every tick.
    public string? LastNotifiedConfigurationLoadError { get; set; }

    // See AgentHeartbeat.ConfigurationVersion/ConfigurationHash
    // (decision-log.md ADR-069).
    public int? ConfigurationVersion { get; set; }
    public string? ConfigurationHash { get; set; }
}
