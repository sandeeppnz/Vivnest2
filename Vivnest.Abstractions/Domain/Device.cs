namespace Vivnest.Abstractions.Domain;

// Admin > Devices pre-registration record (decision-log.md ADR-048/057/058) -
// a real physical/logical device at a Site. Deliberately separate from
// DeviceHeartbeat (live monitoring state) and from DeviceOptions (the MVP
// runtime config blob Program.cs actually reads at Agent startup) - same
// "declared identity vs. live/runtime state" split as every other domain
// class in this codebase. DeviceId is a generated Guid, same convention
// as AgentRegistry/Machine/Capability. DeviceTypeId is a relationship
// property, not validated for existence here - same no-FK-validation
// convention as AgentInstallation's AgentId/MachineId. OwningAgentId IS
// validated - but by the calling service (DeviceService), which checks it
// resolves to a real Agent in the same Tenant/Site before a Device is
// created/updated (ADR-058) - the domain model itself still doesn't reach
// out to storage to check.
//
// No longer carries a CapabilityIds list - which capabilities a Device
// has, and who executes each one, is now DeviceCapability's job (a real
// join with its own ExecutingAgentId), not a flat id list on Device.
//
// Status (ADR-058) replaces the old Enabled bool - a retired Device's
// identity must remain stable (historical DeviceCapability assignments/
// DeviceEvents may still reference its DeviceId), so there is no hard
// delete, only a terminal Retired state - same reasoning Machine already
// established.
//
// RuntimeDeviceId (decision-log.md ADR-063) - the real device-config/*.json
// blob's own "DeviceId" this admin Device corresponds to. ADR-058 already
// documented these as two unrelated identity spaces (this DeviceId is
// server-generated on POST devices-registry-admin; the runtime one is
// hand-authored into a config file/blob); this is the explicit,
// admin-typed link between them - empty means not linked yet, no
// FK/existence validation, same convention every other non-OwningAgentId/
// ExecutingAgentId reference in this codebase follows.
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

    public DeviceStatus Status { get; private set; }

    public string RuntimeDeviceId { get; private set; } = "";

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
        IReadOnlyDictionary<string, string>? settings = null,
        string runtimeDeviceId = "")
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
        RuntimeDeviceId = runtimeDeviceId;
        Settings = settings ?? new Dictionary<string, string>();

        Status = DeviceStatus.Active;
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
        DeviceStatus status,
        string runtimeDeviceId,
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
            Status = status,
            RuntimeDeviceId = runtimeDeviceId,
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
        string runtimeDeviceId,
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
        RuntimeDeviceId = runtimeDeviceId;
        Settings = settings ?? new Dictionary<string, string>();
    }

    public void SetStatus(DeviceStatus status)
    {
        Status = status;
    }
}
