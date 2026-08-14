using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Cloud.Admin.Interfaces;

public enum CapabilityDependencyError
{
    CapabilityNotFound,
    DependsOnCapabilityNotFound,
    SelfReference,
    AlreadyExists,
    CircularDependency
}

public sealed record CapabilityDependencyResult(
    CapabilityDependencyDto? Dependency,
    CapabilityDependencyError? Error,
    string? ErrorMessage);

// Owns the Capability dependency graph (decision-log.md ADR-062, Phase 5) -
// Add/Remove, not plain CRUD, since Add runs real validation (existence,
// self-reference, duplicate, circular dependency).
public interface ICapabilityDependencyService
{
    Task<IReadOnlyList<CapabilityDependencyDto>> ListAllAsync(
        CancellationToken cancellationToken = default);

    Task<CapabilityDependencyResult> AddAsync(
        string capabilityId,
        string dependsOnCapabilityId,
        CancellationToken cancellationToken = default);

    // Returns false if the dependency doesn't exist. No invariant blocks
    // removal - dependency validation only runs at DeviceCapability
    // assignment time, never continuously enforced (spec's own explicit
    // "dependency = validation requirement, not automatic installation").
    Task<bool> RemoveAsync(
        string dependencyId,
        CancellationToken cancellationToken = default);
}
