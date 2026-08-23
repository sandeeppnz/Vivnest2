using Vivnest.Domain.Agents;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Tenants;

namespace Vivnest.Domain.Devices;

// "Can this Capability be assigned to this DeviceType?" (decision-log.md
// ADR-062, Phase 5) - e.g. Camera supports ImageCapture/MotionDetection/
// ObjectDetection, not PowerControl. Global, not tenant-scoped, same
// reasoning as Capability/DeviceType/CapabilityDependency. No Allowed
// bool - row existence is the fact, same shape AgentCapability/
// DeviceCapability already use.
//
// Deliberately distinct from DeviceCapability: this says a Capability
// MAY be assigned to devices of this DeviceType, not that any specific
// Device actually has it. Compatibility never automatically assigns a
// capability to a device (spec's own explicit non-goal) - an admin still
// has to add the DeviceCapability separately.
//
// Existence of the row is the fact, so this hard-deletes
// (CapabilityCompatibilityService.RemoveAsync) - same reasoning
// CapabilityDependency documents.
public sealed class DeviceTypeCapability
{
    public string DeviceTypeCapabilityId { get; private set; } = null!;

    public string DeviceTypeId { get; private set; } = null!;

    public string CapabilityId { get; private set; } = null!;

    private DeviceTypeCapability()
    {
    }

    public DeviceTypeCapability(string deviceTypeId, string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(deviceTypeId))
            throw new ArgumentException("DeviceTypeId is required.", nameof(deviceTypeId));

        if (string.IsNullOrWhiteSpace(capabilityId))
            throw new ArgumentException("CapabilityId is required.", nameof(capabilityId));

        DeviceTypeCapabilityId = Guid.NewGuid().ToString();
        DeviceTypeId = deviceTypeId;
        CapabilityId = capabilityId;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static DeviceTypeCapability Rehydrate(
        string deviceTypeCapabilityId,
        string deviceTypeId,
        string capabilityId)
    {
        return new DeviceTypeCapability
        {
            DeviceTypeCapabilityId = deviceTypeCapabilityId,
            DeviceTypeId = deviceTypeId,
            CapabilityId = capabilityId
        };
    }
}
