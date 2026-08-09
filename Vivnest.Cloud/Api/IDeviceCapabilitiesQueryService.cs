using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Api;

// Reads device-config/agent-config blobs directly - a different data
// source from every other query service here, which all read Table
// Storage (decision-log.md ADR-040). Read-only by decision, matching this
// project's own history (a write/edit config API was built once, went
// unused, and was explicitly removed - ADR-025).
public interface IDeviceCapabilitiesQueryService
{
    Task<DeviceCapabilitiesDto?> GetCapabilitiesAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default);
}
