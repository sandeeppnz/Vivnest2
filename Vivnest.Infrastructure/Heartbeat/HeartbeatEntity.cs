using Azure;
using Azure.Data.Tables;

namespace Vivnest.Infrastructure.Heartbeat;

public class HeartbeatEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public string Status { get; set; } = "";
    public string Version { get; set; } = "";
    public string? BlobName { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? LastCaptureUtc { get; set; }
    public string DeviceId { get; set; } = "";
    public string AgentId { get; set; } = "";

}