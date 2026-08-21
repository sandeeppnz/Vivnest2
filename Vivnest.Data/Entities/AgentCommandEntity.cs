using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Decision-log.md ADR-079 - PartitionKey = TenantId|SiteId, RowKey =
// CommandId, same shape AgentInstallationEntity already established for
// a lifecycle table. Status stored as .ToString(), same convention every
// other status field in this codebase uses. Extends AgentEntity (not
// just BaseEntity) so its inherited AgentId *is* the target Agent -
// same meaning AgentInstallationEntity already gives that property, not
// a separate TargetAgentId field duplicating it.
public sealed class AgentCommandEntity : AgentEntity, ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string? TargetDeviceId { get; set; }

    public string? CapabilityId { get; set; }

    public string CommandType { get; set; } = default!;

    public string Status { get; set; } = default!;

    public string? Payload { get; set; }

    public string? Result { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string RequestedBy { get; set; } = default!;

    public DateTime CreatedUtc { get; set; }

    public DateTime? DispatchedUtc { get; set; }

    public DateTime? ReceivedUtc { get; set; }

    public DateTime? StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public DateTime ExpiresUtc { get; set; }
}
