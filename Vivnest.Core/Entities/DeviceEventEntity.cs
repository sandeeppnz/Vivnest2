using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.Entities;

public class DeviceEventEntity : BaseEntity, ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public string AgentFirmwareVersion { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public string DeviceType { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string? ProcessingStatus { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? ProcessingLastError { get; set; }
    public int ProcessingRetryCount { get; set; }

}