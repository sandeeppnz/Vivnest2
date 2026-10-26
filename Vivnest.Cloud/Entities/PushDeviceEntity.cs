using Azure;
using Azure.Data.Tables;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Entities;

// A phone that has registered to receive push alerts (ADR-129).
// Tenant-scoped like every other registry entity; the push channel itself
// reads across all of them, because Notification carries no tenant.
public sealed class PushDeviceEntity : BaseEntity, ITableEntity
{
    // == $"{TenantId}|{SiteId}".
    public string PartitionKey { get; set; } = default!;

    // == PushDeviceIds.ForToken(ExpoPushToken): derived from the token, so
    // registering the same phone twice updates one row instead of adding a
    // second and sending every alert twice.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    // "ExponentPushToken[...]". A credential in the sense that anyone
    // holding it can push to the phone - never returned by the list API.
    public string ExpoPushToken { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string Platform { get; set; } = "android";

    public bool Enabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }
}
