namespace Vivnest.Core.Domain;

// Admin > Devices pre-registration record (decision-log.md ADR-048/057) -
// a real physical/logical device at a Site. Deliberately separate from
// DeviceHeartbeat (live monitoring state) and from DeviceOptions (the MVP
// runtime config blob Program.cs actually reads at Agent startup) - same
// "declared identity vs. live/runtime state" split as every other domain
// class in this codebase. DeviceId is a generated Guid, same convention
// as AgentRegistry/Machine/Capability. DeviceTypeId/OwningAgentId are
// relationship properties, not validated for existence here - same
// no-FK-validation convention as AgentInstallation's AgentId/MachineId
// (checked by the calling service before a domain object is constructed,
// not by the domain model itself).
//
// No longer carries a CapabilityIds list - which capabilities a Device
// has, and who executes each one, is now DeviceCapability's job (a real
// join with its own ExecutingAgentId), not a flat id list on Device.
public sealed class Device : ISiteScoped
{
    public string TenantId { get; private set; } = null!;

    public string SiteId { get; private set; } = null!;

    public string DeviceId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string DeviceTypeId { get; private set; } = "";

    public string OwningAgentId { get; private set; } = "";

    public string Location { get; private set; } = "";

    public string Brand { get; private set; } = "";

    public string Model { get; private set; } = "";

    public string Firmware { get; private set; } = "";

    public bool Enabled { get; private set; }

    public IReadOnlyDictionary<string, string> Settings { get; private set; } =
        new Dictionary<string, string>();

    private Device()
    {
    }

    public Device(
        string tenantId,
        string siteId,
        string name,
        string deviceTypeId,
        string owningAgentId,
        string location,
        string brand,
        string model,
        string firmware,
        bool enabled,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("SiteId is required.", nameof(siteId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        TenantId = tenantId;
        SiteId = siteId;
        DeviceId = Guid.NewGuid().ToString();
        Name = name;
        DeviceTypeId = deviceTypeId;
        OwningAgentId = owningAgentId;
        Location = location;
        Brand = brand;
        Model = model;
        Firmware = firmware;
        Enabled = enabled;
        Settings = settings ?? new Dictionary<string, string>();
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static Device Rehydrate(
        string tenantId,
        string siteId,
        string deviceId,
        string name,
        string deviceTypeId,
        string owningAgentId,
        string location,
        string brand,
        string model,
        string firmware,
        bool enabled,
        IReadOnlyDictionary<string, string> settings)
    {
        return new Device
        {
            TenantId = tenantId,
            SiteId = siteId,
            DeviceId = deviceId,
            Name = name,
            DeviceTypeId = deviceTypeId,
            OwningAgentId = owningAgentId,
            Location = location,
            Brand = brand,
            Model = model,
            Firmware = firmware,
            Enabled = enabled,
            Settings = settings
        };
    }

    public void Update(
        string name,
        string deviceTypeId,
        string owningAgentId,
        string location,
        string brand,
        string model,
        string firmware,
        bool enabled,
        IReadOnlyDictionary<string, string>? settings)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name;
        DeviceTypeId = deviceTypeId;
        OwningAgentId = owningAgentId;
        Location = location;
        Brand = brand;
        Model = model;
        Firmware = firmware;
        Enabled = enabled;
        Settings = settings ?? new Dictionary<string, string>();
    }
}
