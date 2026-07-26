using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.Entities;

//TODO removed
//public class HeartbeatEntity : ITableEntity
//{
//    public string PartitionKey { get; set; } = "";
//    public string RowKey { get; set; } = "";
//    public string Status { get; set; } = "";
//    public string FirmwareVersion { get; set; } = "";
//    public string? Error { get; set; }
//    public DateTimeOffset? Timestamp { get; set; }
//    public ETag ETag { get; set; }
//    public DateTime LastHeartbeatUtc { get; set; }
//    public DateTime? LastActivityUtc { get; set; }
//    public string DeviceId { get; set; } = "";
//    public string AgentId { get; set; } = "";

//}

public sealed class DeviceHeartbeatEntity : ITableEntity
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
}
