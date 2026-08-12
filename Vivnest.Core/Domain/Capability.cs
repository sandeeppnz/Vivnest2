using Vivnest.Core.Enums;

namespace Vivnest.Core.Domain;

// Admin > Capabilities master list (decision-log.md ADR-042/057) - a
// reusable capability definition ("Image Capture", "Object Detection").
// Global, not tenant-scoped - shared reference data, same reasoning as
// DeviceType. Kept to its existing field shape (no Description/Status
// added here) - nothing has asked this master list to grow beyond what
// CapabilityEntity already tracks, so the domain class doesn't invent
// fields the persistence layer doesn't have a use for yet.
public sealed class Capability
{
    public string CapabilityId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public CapabilityType CapabilityType { get; private set; }

    private Capability()
    {
    }

    public Capability(string name, CapabilityType capabilityType)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        CapabilityId = Guid.NewGuid().ToString();
        Name = name;
        CapabilityType = capabilityType;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static Capability Rehydrate(
        string capabilityId,
        string name,
        CapabilityType capabilityType)
    {
        return new Capability
        {
            CapabilityId = capabilityId,
            Name = name,
            CapabilityType = capabilityType
        };
    }

    public void Update(string name, CapabilityType capabilityType)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name;
        CapabilityType = capabilityType;
    }
}
