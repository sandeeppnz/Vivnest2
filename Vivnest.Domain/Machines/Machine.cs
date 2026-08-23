using Vivnest.Domain.Agents;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Machines;
using Vivnest.Domain.Shared;
using Vivnest.Domain.Tenants;

namespace Vivnest.Domain.Machines;

// The physical/virtual host a Vivnest Agent runs on - "where does the
// software actually execute," distinct from Agent ("what is the stable
// runtime identity") and AgentInstallation ("which deployment of that
// identity is on this host right now"). MachineId is a generated Guid,
// same convention as AgentRegistry/Capability/DeviceType - no operator
// naming/collision concerns, Name carries the memorable label instead.
// If a physical machine is permanently replaced, retire this row
// (SetStatus(Retired/Decommissioned)) and create a new Machine with a
// new MachineId - never reuse an id for different hardware.
public sealed class Machine : ISiteScoped
{
    public string TenantId { get; private set; } = null!;

    public string SiteId { get; private set; } = null!;

    public string MachineId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Hostname { get; private set; }

    public string? Description { get; private set; }

    public MachineStatus Status { get; private set; }

    public string? OperatingSystem { get; private set; }

    public string? Architecture { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    private Machine()
    {
    }

    public Machine(
        string tenantId,
        string siteId,
        string name,
        string? hostname = null,
        string? description = null,
        string? operatingSystem = null,
        string? architecture = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("SiteId is required.", nameof(siteId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        TenantId = tenantId;
        SiteId = siteId;
        MachineId = Guid.NewGuid().ToString();
        Name = name;
        Hostname = hostname;
        Description = description;
        OperatingSystem = operatingSystem;
        Architecture = architecture;

        Status = MachineStatus.Active;

        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static Machine Rehydrate(
        string tenantId,
        string siteId,
        string machineId,
        string name,
        string? hostname,
        string? description,
        MachineStatus status,
        string? operatingSystem,
        string? architecture,
        DateTime createdUtc,
        DateTime updatedUtc)
    {
        return new Machine
        {
            TenantId = tenantId,
            SiteId = siteId,
            MachineId = machineId,
            Name = name,
            Hostname = hostname,
            Description = description,
            Status = status,
            OperatingSystem = operatingSystem,
            Architecture = architecture,
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc
        };
    }

    public void Update(
        string name,
        string? hostname,
        string? description,
        string? operatingSystem,
        string? architecture)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name;
        Hostname = hostname;
        Description = description;
        OperatingSystem = operatingSystem;
        Architecture = architecture;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetStatus(MachineStatus status)
    {
        Status = status;
        UpdatedUtc = DateTime.UtcNow;
    }
}
