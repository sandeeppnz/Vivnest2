using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Cloud.Admin.Interfaces;

public enum CapabilityDeleteError
{
    NotFound,

    // Referenced by a CapabilityDependency or DeviceTypeCapability row -
    // decision-log.md ADR-062. Retire (Status = Retired via UpdateAsync)
    // instead.
    Referenced
}

public sealed record CapabilityDeleteResult(bool Deleted, CapabilityDeleteError? Error, string? ErrorMessage);

public interface ICapabilityManagementService
{
    Task<IReadOnlyList<CapabilityAdminDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<CapabilityAdminDto> CreateAsync(
        string capabilityName,
        string capabilityType,
        IReadOnlyList<CapabilityConfigurationFieldDto>? configurationSchema,
        int? configurationSchemaVersion,
        IReadOnlyDictionary<string, string>? defaultConfiguration,
        string? capabilityKey = null,
        CancellationToken cancellationToken = default);

    Task<CapabilityAdminDto?> UpdateAsync(
        string capabilityId,
        string capabilityName,
        string capabilityType,
        string status,
        IReadOnlyList<CapabilityConfigurationFieldDto>? configurationSchema,
        int? configurationSchemaVersion,
        IReadOnlyDictionary<string, string>? defaultConfiguration,
        string? capabilityKey = null,
        CancellationToken cancellationToken = default);

    // Rejects (Referenced) if a CapabilityDependency or DeviceTypeCapability
    // row still references this CapabilityId - see decision-log.md ADR-062
    // for why this only checks the two new global tables, not tenant-owned
    // DeviceCapability/AgentCapability rows.
    Task<CapabilityDeleteResult> DeleteAsync(
        string capabilityId,
        CancellationToken cancellationToken = default);
}
