namespace Vivnest.Core.Domain;

// Marker for "this belongs to exactly one Tenant/Site" - a persistence
// boundary, not a reason for a domain inheritance hierarchy. BaseEntity
// already satisfies this shape (required TenantId/SiteId), so every
// existing tenant-scoped entity gets it for free.
public interface ISiteScoped
{
    string TenantId { get; }

    string SiteId { get; }
}
