using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.Entities;

public sealed class DeviceHeartbeatEntity : BaseEntity, ITableEntity
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
    public TimeSpan ExpectedActivityInterval { get; set; }

}
