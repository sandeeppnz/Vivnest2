using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// Projects an Admin Device into the shape its real device-config/*.json
// runtime file would have (decision-log.md ADR-063, extended ADR-064) -
// read-only, produces a preview a human diffs against the real file by
// eye. Does not write to Blob Storage, does not touch any real
// device-config file, does not assume RuntimeDeviceId/OwningAgentId's
// RuntimeAgentId are set - missing links are reported as Warnings, not
// errors, so an admin can preview before finishing the mapping.
// Capability projection (ADR-064) runs each assigned DeviceCapability
// through the shared ICapabilityRuntimeProjector registry and keeps only
// each result's DeviceEntry - see IAgentRuntimeConfigurationProjector for
// where AgentEntry goes instead. Renamed from IDeviceConfigurationProjector
// (ADR-063) now that a sibling Agent-scoped projector exists.
public interface IDeviceRuntimeConfigurationProjector
{
    // Returns null only if the Device itself doesn't exist.
    Task<DeviceRuntimeConfigurationDocumentDto?> ProjectAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default);
}
