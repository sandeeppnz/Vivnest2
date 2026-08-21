using Vivnest.Core.Enums;

namespace Vivnest.Abstractions.Domain;

// One edge in the Capability dependency graph (decision-log.md ADR-062,
// Phase 5) - "ObjectDetection requires ImageCapture." Global, not
// tenant-scoped, same reasoning as Capability/DeviceType itself: a
// dependency is a fact about two pieces of shared reference data, not
// about any one tenant's data. Direction is explicit and asymmetric -
// CapabilityId is the dependent, DependsOnCapabilityId is the
// prerequisite; never inferred from the reverse.
//
// Existence of the row is the fact (same as AgentCapability/
// DeviceCapability's join-row-is-the-fact shape), so this hard-deletes
// (CapabilityDependencyService.RemoveAsync) rather than soft-removing -
// unlike an assignment, a dependency edge has no history worth keeping
// and removing one doesn't retroactively invalidate anything (dependency
// validation only runs at DeviceCapability-assignment time, never
// continuously enforced).
public sealed class CapabilityDependency
{
    public string DependencyId { get; private set; } = null!;

    public string CapabilityId { get; private set; } = null!;

    public string DependsOnCapabilityId { get; private set; } = null!;

    public CapabilityDependencyType DependencyType { get; private set; }

    private CapabilityDependency()
    {
    }

    public CapabilityDependency(
        string capabilityId,
        string dependsOnCapabilityId,
        CapabilityDependencyType dependencyType = CapabilityDependencyType.Required)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
            throw new ArgumentException("CapabilityId is required.", nameof(capabilityId));

        if (string.IsNullOrWhiteSpace(dependsOnCapabilityId))
            throw new ArgumentException("DependsOnCapabilityId is required.", nameof(dependsOnCapabilityId));

        if (capabilityId == dependsOnCapabilityId)
            throw new ArgumentException("A capability cannot depend on itself.", nameof(dependsOnCapabilityId));

        DependencyId = Guid.NewGuid().ToString();
        CapabilityId = capabilityId;
        DependsOnCapabilityId = dependsOnCapabilityId;
        DependencyType = dependencyType;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static CapabilityDependency Rehydrate(
        string dependencyId,
        string capabilityId,
        string dependsOnCapabilityId,
        CapabilityDependencyType dependencyType)
    {
        return new CapabilityDependency
        {
            DependencyId = dependencyId,
            CapabilityId = capabilityId,
            DependsOnCapabilityId = dependsOnCapabilityId,
            DependencyType = dependencyType
        };
    }
}
