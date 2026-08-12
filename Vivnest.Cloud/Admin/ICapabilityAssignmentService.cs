using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin;

// Owns the DeviceCapability lifecycle (decision-log.md ADR-057) - Assign/
// Update/Unassign, not plain CRUD, same reasoning
// IAgentInstallationManagementService documents for Install/Move/Uninstall.
public interface ICapabilityAssignmentService
{
    Task<IReadOnlyList<DeviceCapabilityDto>> ListByDeviceAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default);

    // Returns null if DeviceId or CapabilityId doesn't exist, or this
    // (Device, Capability) pair already has an active assignment - use
    // UpdateAssignmentAsync to change ExecutingAgentId/Enabled/Settings on
    // an existing one instead.
    Task<DeviceCapabilityDto?> AssignAsync(
        TenantContext tenant,
        string deviceId,
        string capabilityId,
        string? executingAgentId,
        bool enabled,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default);

    Task<DeviceCapabilityDto?> UpdateAssignmentAsync(
        TenantContext tenant,
        string deviceCapabilityId,
        string? executingAgentId,
        bool enabled,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default);

    // Returns null if this (Device, Capability) pair has no active
    // assignment.
    Task<DeviceCapabilityDto?> UnassignAsync(
        TenantContext tenant,
        string deviceId,
        string capabilityId,
        CancellationToken cancellationToken = default);
}
