using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Abstractions.Models.Agent;

// An Agent's declared capability manifest (decision-log.md ADR-059,
// "Phase 4 - Capability model") - "this Agent has the ability to execute
// Capability X," independent of any specific device. Replaces the old
// flat AgentRegistryEntity.CapabilityIds list (ADR-046), same reasoning
// ADR-057 already used to replace Device's own flat CapabilityIds with
// DeviceCapability: a real join with its own lifecycle beats a
// comma-separated id list once something needs to query/validate against
// it, which this ADR's actual reason for existing (see below) now does.
//
// This is the other half of the relationship DeviceCapability.ExecutingAgentId
// only names, never validates: assigning Capability X to a Device with
// ExecutingAgentId = A doesn't check that A can actually run X - it just
// checks A exists. AgentCapability is what CapabilityAssignmentService
// now checks against to close that gap ("A001 does not have
// ObjectDetection capability" becomes a real, enforced rejection, not
// just an unvalidated assumption).
//
// Modeled after DeviceCapability, not the flat master lists - a
// declaration is a lifecycle (Assign/Unassign), not reference data, so
// it soft-removes (Status) rather than hard-deleting, same reasoning.
// No Settings/Enabled the way DeviceCapability has - nothing about "can
// this Agent run X" needs per-assignment configuration or a separate
// on/off switch; Status alone (Active/Removed) covers it.
public sealed class AgentCapability : ISiteScoped
{
    public string TenantId { get; private set; } = null!;

    public string SiteId { get; private set; } = null!;

    public string AgentCapabilityId { get; private set; } = null!;

    public string AgentId { get; private set; } = null!;

    public string CapabilityId { get; private set; } = null!;

    public AgentCapabilityStatus Status { get; private set; }

    public DateTime AssignedUtc { get; private set; }

    public DateTime? RemovedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    private AgentCapability()
    {
    }

    public AgentCapability(
        string tenantId,
        string siteId,
        string agentId,
        string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("SiteId is required.", nameof(siteId));

        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("AgentId is required.", nameof(agentId));

        if (string.IsNullOrWhiteSpace(capabilityId))
            throw new ArgumentException("CapabilityId is required.", nameof(capabilityId));

        TenantId = tenantId;
        SiteId = siteId;
        AgentCapabilityId = Guid.NewGuid().ToString();
        AgentId = agentId;
        CapabilityId = capabilityId;

        Status = AgentCapabilityStatus.Active;

        AssignedUtc = DateTime.UtcNow;
        UpdatedUtc = AssignedUtc;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static AgentCapability Rehydrate(
        string tenantId,
        string siteId,
        string agentCapabilityId,
        string agentId,
        string capabilityId,
        AgentCapabilityStatus status,
        DateTime assignedUtc,
        DateTime? removedUtc,
        DateTime updatedUtc)
    {
        return new AgentCapability
        {
            TenantId = tenantId,
            SiteId = siteId,
            AgentCapabilityId = agentCapabilityId,
            AgentId = agentId,
            CapabilityId = capabilityId,
            Status = status,
            AssignedUtc = assignedUtc,
            RemovedUtc = removedUtc,
            UpdatedUtc = updatedUtc
        };
    }

    // Marks this declaration Removed - same "retire, never mutate
    // history" reasoning as AgentInstallation.Remove()/DeviceCapability.Remove().
    public void Remove()
    {
        Status = AgentCapabilityStatus.Removed;
        RemovedUtc = DateTime.UtcNow;
        UpdatedUtc = RemovedUtc.Value;
    }
}
