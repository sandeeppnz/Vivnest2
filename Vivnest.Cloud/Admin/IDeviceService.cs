using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin;

// Application-layer service for the Device domain concept (decision-log.md
// ADR-057/058) - renamed from IDeviceRegistryManagementService now that
// Device is a real domain class, not just DeviceRegistryEntity built
// directly. The underlying table/entity (tblDeviceRegistry/
// DeviceRegistryEntity) keeps its existing name - persistence naming is a
// repository concern, independent of this rename, same reasoning that
// kept tblAgentRegistry's name when the Agent domain concept was discussed.
//
// No DeleteAsync (ADR-058) - a Device's identity must remain stable
// (historical DeviceCapability assignments/DeviceEvents may still
// reference its DeviceId), so retiring one sets Status: Retired instead
// of removing the row, same reasoning IMachineManagementService already
// established.
public interface IDeviceService
{
    // ownerAgentId/deviceTypeId are optional server-side filters (ADR-058)
    // - "devices owned by this agent" / "devices of this type" - on top of
    // the tenant/site scoping every List already had.
    Task<IReadOnlyList<DeviceRegistryDto>> ListAsync(
        TenantContext tenant,
        string? ownerAgentId = null,
        string? deviceTypeId = null,
        CancellationToken cancellationToken = default);

    // Returns null if OwningAgentId is non-empty but doesn't resolve to a
    // real Agent in this tenant/site (ADR-058) - "a Device's owning Agent
    // must belong to the same Tenant/Site" as an authorization boundary.
    Task<DeviceRegistryDto?> CreateAsync(
        TenantContext tenant,
        string name,
        string deviceTypeId,
        string owningAgentId,
        string location,
        string brand,
        string model,
        string firmware,
        string? runtimeDeviceId,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default);

    // Returns null if the Device doesn't exist, OR if OwningAgentId is
    // non-empty but doesn't resolve to a real Agent in this tenant/site -
    // same collapsed-reasons convention AgentInstallationManagementService.MoveAsync
    // already uses for "doesn't exist," not distinguished further since no
    // caller needs to tell the two apart.
    Task<DeviceRegistryDto?> UpdateAsync(
        TenantContext tenant,
        string deviceId,
        string name,
        string deviceTypeId,
        string owningAgentId,
        string location,
        string brand,
        string model,
        string firmware,
        string status,
        string? runtimeDeviceId,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default);
}
