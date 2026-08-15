using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// Writes a Device's projected runtime configuration document to its real
// device-config/{runtimeDeviceId}.json blob (decision-log.md ADR-064) -
// the write-path counterpart to IDeviceRuntimeConfigurationProjector's
// read-only preview. Hard-gated: refuses to publish while the projection
// carries any Warnings (unresolved RuntimeDeviceId/DeviceType/
// OwningAgentId, or a capability with no registered runtime projector).
public interface IDeviceRuntimeConfigurationPublisher
{
    // Returns null only if the Device itself doesn't exist.
    Task<DevicePublishResult?> PublishAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default);
}

// Published is false whenever a gate blocked the write (Reason explains
// why) - this is an expected, well-formed outcome for a caller to render,
// not an error. Document always reflects the latest projection (including
// any informational warnings the credential-stripping guard added), even
// when Published is false, so a caller can show exactly what's blocking
// the publish.
public sealed record DevicePublishResult(
    bool Published,
    DeviceRuntimeConfigurationDocumentDto Document,
    string? Reason);
