using Vivnest.Core.Enums;

namespace Vivnest.Core.Domain;

// The stable runtime identity of a Vivnest Agent - "who" runs, distinct
// from Machine ("where it runs") and AgentInstallation ("which deployment
// of it is on that Machine right now"). AgentId is a generated Guid, same
// convention as Machine/Capability/DeviceType. Backed by AgentRegistryEntity
// (decision-log.md ADR-043/053) - that entity is also the persistence
// representation for this exact domain concept, not a separate one; see
// AgentRegistryEntity's own comment for why a second tblAgents table was
// never stood up.
//
// CapabilityIds are this Agent's own declared capabilities (ADR-046) -
// unrelated to DeviceCapability.ExecutingAgentId (ADR-057), which is a
// capability *assignment* pointing at an Agent, not a capability the
// Agent itself declares having. Kept as a flat id list here deliberately
// - nothing in this ADR asked Agent's own capability declaration to
// become a real join the way Device's did.
public sealed class Agent : ISiteScoped
{
    public string TenantId { get; private set; } = null!;

    public string SiteId { get; private set; } = null!;

    public string AgentId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public AgentStatus Status { get; private set; }

    public string FirmwareVersion { get; private set; } = null!;

    public AgentType Type { get; private set; }

    public IReadOnlyList<string> CapabilityIds { get; private set; } = [];

    public DateTime CreatedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    private Agent()
    {
    }

    public Agent(
        string tenantId,
        string siteId,
        string name,
        string? description,
        string firmwareVersion,
        AgentType type,
        IReadOnlyList<string>? capabilityIds = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("SiteId is required.", nameof(siteId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        TenantId = tenantId;
        SiteId = siteId;
        AgentId = Guid.NewGuid().ToString();
        Name = name;
        Description = description;
        FirmwareVersion = firmwareVersion;
        Type = type;
        CapabilityIds = capabilityIds ?? [];

        Status = AgentStatus.Active;

        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static Agent Rehydrate(
        string tenantId,
        string siteId,
        string agentId,
        string name,
        string? description,
        AgentStatus status,
        string firmwareVersion,
        AgentType type,
        IReadOnlyList<string> capabilityIds,
        DateTime createdUtc,
        DateTime updatedUtc)
    {
        return new Agent
        {
            TenantId = tenantId,
            SiteId = siteId,
            AgentId = agentId,
            Name = name,
            Description = description,
            Status = status,
            FirmwareVersion = firmwareVersion,
            Type = type,
            CapabilityIds = capabilityIds,
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc
        };
    }

    public void Update(
        string name,
        string? description,
        string firmwareVersion,
        AgentType type,
        IReadOnlyList<string>? capabilityIds)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name;
        Description = description;
        FirmwareVersion = firmwareVersion;
        Type = type;
        CapabilityIds = capabilityIds ?? [];
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetStatus(AgentStatus status)
    {
        Status = status;
        UpdatedUtc = DateTime.UtcNow;
    }
}
