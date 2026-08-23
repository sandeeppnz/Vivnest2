using Azure;
using Azure.Data.Tables;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Entities;

public sealed class ApiKeyEntity : BaseEntity, ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string? Name { get; set; }

    // Non-secret handle for managing this key (list/revoke) without ever
    // exposing the hash it's actually looked up by on the auth path.
    public string KeyId { get; set; } = default!;

    public bool Enabled { get; set; }

    // Defaults to false (unrestricted) so keys created before this field
    // existed - absent from storage, deserializing to the CLR default -
    // keep their existing full access instead of silently losing it.
    public bool DevicesOnly { get; set; }

    // Null for an ordinary tenant/dashboard key. Set to a RuntimeAgentId
    // for a key minted at registration and handed to one specific Agent,
    // which is how the Agent-facing command callbacks authenticate
    // themselves. An agent key is scoped to exactly that Agent - it is
    // not a general tenant credential.
    public string? AgentId { get; set; }

    public DateTime CreatedUtc { get; set; }
}
