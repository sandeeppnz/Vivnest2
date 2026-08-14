using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin;

public enum CapabilityAssignmentErrorCode
{
    DeviceNotFound,
    CapabilityNotFound,

    // UpdateAssignmentAsync only - the DeviceCapabilityId itself doesn't exist.
    AssignmentNotFound,

    // AssignAsync only (decision-log.md ADR-062) - Capability isn't in
    // this Device's DeviceType's compatibility set, or the Device has no
    // DeviceTypeId set at all (nothing to check compatibility against).
    IncompatibleDeviceType,

    // Doesn't exist for this tenant/site, or doesn't declare this Capability (ADR-058/059).
    ExecutingAgentInvalid,

    // AssignAsync only (ADR-062) - a direct CapabilityDependency isn't
    // satisfied by an active DeviceCapability on this Device.
    MissingDependency,

    // AssignAsync only - this (Device, Capability) pair already has an active assignment.
    AlreadyAssigned,

    // Settings (after merging Capability defaults) fail
    // CapabilityConfigurationService.Validate (ADR-062).
    InvalidConfiguration
}

public sealed record CapabilityAssignmentResult(
    DeviceCapabilityDto? DeviceCapability,
    CapabilityAssignmentErrorCode? Error,
    string? ErrorMessage);

// Owns the DeviceCapability lifecycle (decision-log.md ADR-057/062) - Assign/
// Update/Unassign, not plain CRUD, same reasoning
// IAgentInstallationManagementService documents for Install/Move/Uninstall.
public interface ICapabilityAssignmentService
{
    Task<IReadOnlyList<DeviceCapabilityDto>> ListByDeviceAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default);

    // Runs the full assignment algorithm (decision-log.md ADR-062 §21/44):
    // Device/Capability exist, Capability compatible with the Device's
    // DeviceType, ExecutingAgent valid + declares this Capability, direct
    // dependencies satisfied, supplied Settings merged with Capability
    // defaults and validated against its ConfigurationSchema, at most one
    // active assignment per (Device, Capability) pair.
    Task<CapabilityAssignmentResult> AssignAsync(
        TenantContext tenant,
        string deviceId,
        string capabilityId,
        string? executingAgentId,
        bool enabled,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default);

    // Narrower than AssignAsync - re-validates only what Update can
    // actually change (ExecutingAgent, Settings). DeviceType compatibility
    // and dependency-satisfaction were already true when this assignment
    // was created and don't change from an Update.
    Task<CapabilityAssignmentResult> UpdateAssignmentAsync(
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
