using Vivnest.Core.Enums;

namespace Vivnest.Core.Domain;

// A physical/logical operational location belonging to exactly one Tenant
// - "where does this Vivnest environment operate." TenantId is set once at
// construction and never exposed with a setter - moving a Site between
// Tenants is deliberately not supported by mutation; if that's ever needed
// it should be an explicit operation (e.g. recreate + migrate references),
// not a property set that could silently orphan every site-scoped row
// still keyed to the old TenantId|SiteId partition. SiteId is a generated
// Guid (not caller-chosen), same reasoning as Tenant.TenantId.
public sealed class Site : ISiteScoped
{
    public string TenantId { get; private set; } = null!;

    public string SiteId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public SiteStatus Status { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    private Site()
    {
    }

    public Site(string tenantId, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        TenantId = tenantId;
        SiteId = Guid.NewGuid().ToString();
        Name = name;
        Description = description;

        Status = SiteStatus.Active;

        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static Site Rehydrate(
        string tenantId,
        string siteId,
        string name,
        string? description,
        SiteStatus status,
        DateTime createdUtc,
        DateTime updatedUtc)
    {
        return new Site
        {
            TenantId = tenantId,
            SiteId = siteId,
            Name = name,
            Description = description,
            Status = status,
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc
        };
    }

    public void Update(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name;
        Description = description;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetStatus(SiteStatus status)
    {
        Status = status;
        UpdatedUtc = DateTime.UtcNow;
    }
}
