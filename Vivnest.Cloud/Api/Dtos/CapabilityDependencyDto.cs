namespace Vivnest.Cloud.Api.Dtos;

// Admin > Capability dependency graph (decision-log.md ADR-062, Phase 5) -
// "ObjectDetection requires ImageCapture." Global, not tenant-scoped,
// same reasoning as CapabilityAdminDto.
public sealed record CapabilityDependencyDto(
    Guid DependencyId,
    Guid CapabilityId,
    Guid DependsOnCapabilityId,
    string DependencyType);

public sealed record AddCapabilityDependencyRequest(
    string CapabilityId,
    string DependsOnCapabilityId);
