using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// Computes Desired/Published/Applied status (decision-log.md ADR-068) for
// an already-projected document - "Desired" is that same document, never
// re-fetched here. Returns null when there's nothing coherent to report
// (the document has unresolved Warnings, or no RuntimeDeviceId/
// RuntimeAgentId yet) rather than guessing.
public interface IConfigurationSyncStatusService
{
    Task<ConfigurationSyncStatusDto?> GetDeviceStatusAsync(
        TenantContext tenant,
        DeviceRuntimeConfigurationDocumentDto document,
        CancellationToken cancellationToken = default);

    Task<ConfigurationSyncStatusDto?> GetAgentStatusAsync(
        TenantContext tenant,
        AgentRuntimeConfigurationDocumentDto document,
        CancellationToken cancellationToken = default);
}
