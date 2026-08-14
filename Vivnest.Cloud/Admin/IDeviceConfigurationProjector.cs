using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin;

// Projects an Admin Device into the shape its real device-config/*.json
// runtime file would have (decision-log.md ADR-063) - read-only, produces
// a preview a human diffs against the real file by eye. Does not write to
// Blob Storage, does not touch any real device-config file, does not
// assume RuntimeDeviceId/OwningAgentId's RuntimeAgentId are set - missing
// links are reported as Warnings, not errors, so an admin can preview
// before finishing the mapping.
public interface IDeviceConfigurationProjector
{
    // Returns null only if the Device itself doesn't exist.
    Task<ProjectedDeviceConfigDto?> ProjectAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default);
}
