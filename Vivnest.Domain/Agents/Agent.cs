using Vivnest.Domain.Shared;
namespace Vivnest.Domain.Agents;

// The stable runtime identity of a Vivnest Agent - "who" runs, distinct
// from Machine ("where it runs") and AgentInstallation ("which deployment
// of it is on that Machine right now"). AgentId is a generated Guid, same
// convention as Machine/Capability/DeviceType. Backed by AgentRegistryEntity
// (decision-log.md ADR-043/053) - that entity is also the persistence
// representation for this exact domain concept, not a separate one; see
// AgentRegistryEntity's own comment for why a second tblAgents table was
// never stood up.
//
// No longer carries a CapabilityIds list (decision-log.md ADR-059) - an
// Agent's declared capabilities are now AgentCapability's job (a real
// join with its own lifecycle), not a flat id list here - same move
// ADR-057 already made for Device.CapabilityIds -> DeviceCapability.
//
// RuntimeAgentId (decision-log.md ADR-063) - the real Vivnest.Agent
// process's own appsettings.json "Agent:AgentId" value this admin Agent
// corresponds to. Two unrelated identity spaces, same problem
// ADR-058 already documented for Device: AgentId here is server-generated
// on POST agents-registry-admin; the runtime one is hand-typed into a
// config file. Empty means not linked yet - admin-typed, no FK/existence
// validation, same "no validation on this id" convention every other
// non-OwningAgentId/ExecutingAgentId reference in this codebase follows.
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

    public string RuntimeAgentId { get; private set; } = "";

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
        AgentType type)
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
        string runtimeAgentId,
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
            RuntimeAgentId = runtimeAgentId,
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc
        };
    }

    public void Update(
        string name,
        string? description,
        string firmwareVersion,
        AgentType type,
        string runtimeAgentId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name;
        Description = description;
        FirmwareVersion = firmwareVersion;
        Type = type;
        RuntimeAgentId = runtimeAgentId;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetStatus(AgentStatus status)
    {
        Status = status;
        UpdatedUtc = DateTime.UtcNow;
    }
}
