using Vivnest.Core.Enums;

namespace Vivnest.Core.Domain;

// Admin > Device Types master list (decision-log.md ADR-047/057) - the
// category/kind a Device belongs to ("Camera", "SmartPlug"), deliberately
// separate from Vivnest.Core.Enums.DeviceType, the fixed enum real Agent
// code branches on - see DeviceTypeEntity's own comment for why these are
// two unrelated concepts sharing one English word. Named
// DeviceTypeDefinition, not DeviceType, for a very concrete reason: naming
// it DeviceType produces a real C# ambiguous-reference compile error
// (CS0104) in every file that already has both `using Vivnest.Core.Domain;`
// and `using Vivnest.Core.Enums;` in scope - confirmed live, it broke
// Vivnest.Infrastructure/DataStores/Helpers/DeviceHeartbeatMapping.cs,
// unrelated existing code on the real heartbeat write path, not just this
// new file. Global, not tenant-scoped - a device type is shared reference
// data, same reasoning as Capability. DeviceTypeId is a generated Guid,
// same convention as every other master-list id in this codebase.
public sealed class DeviceTypeDefinition
{
    public string DeviceTypeId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public DeviceTypeStatus Status { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    private DeviceTypeDefinition()
    {
    }

    public DeviceTypeDefinition(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        DeviceTypeId = Guid.NewGuid().ToString();
        Name = name;
        Description = description;

        Status = DeviceTypeStatus.Active;

        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static DeviceTypeDefinition Rehydrate(
        string deviceTypeId,
        string name,
        string? description,
        DeviceTypeStatus status,
        DateTime createdUtc,
        DateTime updatedUtc)
    {
        return new DeviceTypeDefinition
        {
            DeviceTypeId = deviceTypeId,
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

    public void SetStatus(DeviceTypeStatus status)
    {
        Status = status;
        UpdatedUtc = DateTime.UtcNow;
    }
}
