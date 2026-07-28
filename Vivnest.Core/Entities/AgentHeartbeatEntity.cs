using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.Entities;

public sealed class AgentHeartbeatEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime LastHeartbeatUtc { get; set; }

    public string FirmwareVersion { get; set; } = default!;

    public string HostName { get; set; } = default!;

    public string? Error { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }
}
