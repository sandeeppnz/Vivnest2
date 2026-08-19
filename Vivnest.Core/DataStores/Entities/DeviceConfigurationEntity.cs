using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Decision-log.md ADR-069 - Published-state metadata for the new
// immutable versioned device-config blob layout
// (device-config/{runtimeDeviceId}/versions/{n}.json +
// .../current.json). Deliberately tracks only what's actually
// authoritative here and nowhere else: the latest published version
// number and its content hash. Desired is never persisted (it's always
// the live-projected document, same principle ADR-068 established) and
// Applied still lives on DeviceHeartbeat - this entity is not a
// duplicate of either.
public sealed class DeviceConfigurationEntity : BaseEntity, ITableEntity, IConfigurationStateEntity
{
    // == $"{TenantId}|{SiteId}" - same convention DeviceHeartbeatEntity/
    // AgentHeartbeatEntity already use for a tenant/site-scoped identity
    // row.
    public string PartitionKey { get; set; } = default!;

    // == RuntimeDeviceId - configuration identity is independent of the
    // physical machine and independent of the admin DeviceId, exactly as
    // decision-log.md's own "configuration identity" principle requires.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    // Read back before every publish and passed to AzureTableStore.UpdateAsync
    // - this is what actually enforces "don't silently lose a concurrent
    // Admin publish" (a stale ETag on write throws a 412, caught and
    // retried by the publisher).
    public ETag ETag { get; set; }

    public int CurrentVersion { get; set; }

    public string CurrentHash { get; set; } = default!;

    public DateTime PublishedUtc { get; set; }
}
