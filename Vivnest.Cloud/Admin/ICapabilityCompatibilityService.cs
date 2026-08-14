using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Cloud.Admin;

public enum CapabilityCompatibilityError
{
    DeviceTypeNotFound,
    CapabilityNotFound,
    AlreadyExists
}

public sealed record CapabilityCompatibilityResult(
    DeviceTypeCapabilityDto? Compatibility,
    CapabilityCompatibilityError? Error,
    string? ErrorMessage);

// Owns "can this Capability be assigned to this DeviceType?" (decision-
// log.md ADR-062, Phase 5) - Add/Remove, not plain CRUD, matching
// ICapabilityDependencyService's shape.
public interface ICapabilityCompatibilityService
{
    Task<IReadOnlyList<DeviceTypeCapabilityDto>> ListAllAsync(
        CancellationToken cancellationToken = default);

    Task<CapabilityCompatibilityResult> AddAsync(
        string deviceTypeId,
        string capabilityId,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        string deviceTypeCapabilityId,
        CancellationToken cancellationToken = default);
}
