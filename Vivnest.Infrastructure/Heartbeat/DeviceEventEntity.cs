using Azure;
using Azure.Data.Tables;

namespace Vivnest.Infrastructure.Heartbeat;

public class DeviceEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public string AgentId { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public string DeviceType { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public DateTime EventTimestampUtc { get; set; }

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }
}