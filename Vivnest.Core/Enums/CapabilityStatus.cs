namespace Vivnest.Core.Enums;

// Capability master-list lifecycle (decision-log.md ADR-062, Phase 5) -
// same "Retired, not deleted" reasoning as DeviceStatus/MachineStatus: a
// Capability referenced by a CapabilityDependency or DeviceTypeCapability
// row can't be hard-deleted (CapabilityManagementService.DeleteAsync
// rejects it), so retiring is the way to take one out of use without
// breaking those references.
public enum CapabilityStatus
{
    Active,
    Retired
}
