using Azure;
using Azure.Data.Tables;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Entities;

// Mirrors DeviceConfigurationEntity exactly (decision-log.md ADR-069),
// for the Agent-config side (agent-config/{runtimeAgentId}/versions/{n}.json
// + .../current.json).
public sealed class AgentConfigurationEntity : BaseEntity, ITableEntity, IConfigurationStateEntity
{
    public string PartitionKey { get; set; } = default!;

    // == RuntimeAgentId.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public int CurrentVersion { get; set; }

    public string CurrentHash { get; set; } = default!;

    public DateTime PublishedUtc { get; set; }
}
