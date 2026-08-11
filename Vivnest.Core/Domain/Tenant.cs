using Vivnest.Core.Enums;

namespace Vivnest.Core.Domain;

// The top-level ownership boundary - "who owns this Vivnest environment."
// Deliberately persistence-agnostic: TenantManagementService maps this to
// and from TenantEntity for storage, so Table Storage could theoretically
// be swapped later without this type changing. Not an aggregate root over
// Site/Machine/Agent/Device - those get their own repositories, referencing
// TenantId as plain data, not a navigable collection here.
public sealed class Tenant
{
    public string TenantId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public TenantStatus Status { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    private Tenant()
    {
    }

    public Tenant(string tenantId, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        TenantId = tenantId;
        Name = name;
        Description = description;

        Status = TenantStatus.Active;

        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    // Rehydrates from storage - bypasses the validating constructor since
    // an already-persisted row is trusted, and needs to restore CreatedUtc/
    // Status/etc. rather than resetting them.
    public static Tenant Rehydrate(
        string tenantId,
        string name,
        string? description,
        TenantStatus status,
        DateTime createdUtc,
        DateTime updatedUtc)
    {
        return new Tenant
        {
            TenantId = tenantId,
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

    public void SetStatus(TenantStatus status)
    {
        Status = status;
        UpdatedUtc = DateTime.UtcNow;
    }
}
