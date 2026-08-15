using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Core.DataStores.Entities;

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

    // Decision-log.md ADR-075 (Phase 8 Pass 2) - for a caller (the main
    // Agent/Device list/detail endpoints) that only needs the real
    // Published-vs-Applied comparison, not a full projected document. Skips
    // the "requires a coherent projection + no Warnings" precondition the
    // two methods above have - RuntimeDeviceId/RuntimeAgentId is just the
    // heartbeat row's own RowKey (both heartbeat writers already stamp the
    // resolved runtime identity there), and Applied/Version/Hash/LoadError
    // already sit on the row directly - only Desired (the manifest/blob
    // read) needs fetching. Never null - there's no "incoherent projection"
    // case to gate on here, worst case is a real NeverPublished.
    Task<ConfigurationSyncStatusDto> GetDeviceStatusFromHeartbeatAsync(
        TenantContext tenant,
        DeviceHeartbeatEntity device,
        string? applyError,
        CancellationToken cancellationToken = default);

    Task<ConfigurationSyncStatusDto> GetAgentStatusFromHeartbeatAsync(
        TenantContext tenant,
        AgentHeartbeatEntity agent,
        CancellationToken cancellationToken = default);
}
