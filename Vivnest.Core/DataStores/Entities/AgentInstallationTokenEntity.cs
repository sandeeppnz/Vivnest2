using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Decision-log.md ADR-071 - a short-lived, single-use credential handed to
// an operator provisioning a fresh Machine, so a not-yet-trusted process
// (Vivnest.Agent.Updater, before it has a RuntimeAgentId or any tenant
// x-api-key) can prove it's allowed to register a specific
// AgentInstallation. Mirrors ApiKeyEntity's exact shape: PartitionKey is
// the token's own hash (never the raw value), giving the registration
// endpoint an O(1) lookup with no tenant context needed - the token itself
// is the trust, same reasoning ApiKeyAuthenticator already established for
// tenant keys.
public sealed class AgentInstallationTokenEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string TenantId { get; set; } = default!;

    public string SiteId { get; set; } = default!;

    public string InstallationId { get; set; } = default!;

    public DateTime ExpiresUtc { get; set; }

    // Single-use: the registration endpoint sets this true on first
    // successful use rather than deleting the row - keeps a real audit
    // trail of exactly when a Machine actually registered, same
    // "never destroy history" instinct as AgentInstallation.Decommission()
    // creating a new row instead of mutating the old one.
    public bool Used { get; set; }

    public DateTime CreatedUtc { get; set; }
}
