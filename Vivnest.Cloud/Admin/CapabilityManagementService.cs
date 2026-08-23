using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Admin;

// Maps the persistence-agnostic Capability domain model (Vivnest.Core.Domain)
// to/from CapabilityEntity for storage (decision-log.md ADR-057/062). A
// genuine hard delete, unlike ApiKeyManagementService.RevokeAsync's
// Enabled=false soft-delete - Capability is master/reference data meant to
// actually shrink, not an audit trail - UNLESS a CapabilityDependency/
// DeviceTypeCapability still references it (ADR-062), in which case
// DeleteAsync rejects and the caller should retire (Status = Retired)
// instead. CapabilityType/Status string validation (Enum.TryParse)
// happens in the Function layer before calling here - this service
// trusts already-validated input, matching ApiKeysFunction's
// validate-then-call-service pattern.
public sealed class CapabilityManagementService : ICapabilityManagementService
{
    private static readonly IReadOnlyList<CapabilityConfigurationField> EmptySchema =
        Array.Empty<CapabilityConfigurationField>();

    private static readonly IReadOnlyDictionary<string, string> EmptyDefaults =
        new Dictionary<string, string>();

    private readonly ICapabilityStore _capabilities;
    private readonly ICapabilityDependencyStore _dependencies;
    private readonly IDeviceTypeCapabilityStore _compatibility;

    public CapabilityManagementService(
        ICapabilityStore capabilities,
        ICapabilityDependencyStore dependencies,
        IDeviceTypeCapabilityStore compatibility)
    {
        _capabilities = capabilities;
        _dependencies = dependencies;
        _compatibility = compatibility;
    }

    public async Task<IReadOnlyList<CapabilityAdminDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var entities = await _capabilities.ListAsync(cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<CapabilityAdminDto> CreateAsync(
        string capabilityName,
        string capabilityType,
        IReadOnlyList<CapabilityConfigurationFieldDto>? configurationSchema,
        int? configurationSchemaVersion,
        IReadOnlyDictionary<string, string>? defaultConfiguration,
        string? capabilityKey = null,
        CancellationToken cancellationToken = default)
    {
        var capability = new Capability(
            capabilityName,
            Enum.Parse<CapabilityType>(capabilityType),
            ToDomainSchema(configurationSchema),
            configurationSchemaVersion ?? 1,
            defaultConfiguration,
            capabilityKey);

        var entity = ToEntity(capability);

        await _capabilities.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<CapabilityAdminDto?> UpdateAsync(
        string capabilityId,
        string capabilityName,
        string capabilityType,
        string status,
        IReadOnlyList<CapabilityConfigurationFieldDto>? configurationSchema,
        int? configurationSchemaVersion,
        IReadOnlyDictionary<string, string>? defaultConfiguration,
        string? capabilityKey = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _capabilities.GetAsync(capabilityId, cancellationToken);

        if (entity == null)
            return null;

        var capability = ToDomain(entity);
        capability.Update(
            capabilityName,
            Enum.Parse<CapabilityType>(capabilityType),
            Enum.Parse<CapabilityStatus>(status),
            ToDomainSchema(configurationSchema),
            configurationSchemaVersion ?? capability.ConfigurationSchemaVersion,
            defaultConfiguration,
            capabilityKey);

        var updated = ToEntity(capability);
        updated.ETag = entity.ETag;

        await _capabilities.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    public async Task<CapabilityDeleteResult> DeleteAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _capabilities.GetAsync(capabilityId, cancellationToken);

        if (entity == null)
            return new CapabilityDeleteResult(false, CapabilityDeleteError.NotFound, "Capability not found.");

        var allDependencies = await _dependencies.ListAsync(cancellationToken);
        var isDependencyReferenced = allDependencies.Any(
            d => d.CapabilityId == capabilityId || d.DependsOnCapabilityId == capabilityId);
        var allCompatibility = await _compatibility.ListAsync(cancellationToken);
        var isCompatibilityReferenced = allCompatibility.Any(c => c.CapabilityId == capabilityId);

        if (isDependencyReferenced || isCompatibilityReferenced)
        {
            return new CapabilityDeleteResult(
                false,
                CapabilityDeleteError.Referenced,
                "This capability is referenced by a dependency or compatibility record - retire it " +
                "(Status = Retired) instead of deleting.");
        }

        await _capabilities.DeleteAsync(capabilityId, cancellationToken);

        return new CapabilityDeleteResult(true, null, null);
    }

    private static Capability ToDomain(CapabilityEntity entity)
    {
        return Capability.Rehydrate(
            entity.RowKey,
            entity.CapabilityName,
            entity.CapabilityKey,
            Enum.Parse<CapabilityType>(entity.CapabilityType),
            string.IsNullOrWhiteSpace(entity.Status) ? CapabilityStatus.Active : Enum.Parse<CapabilityStatus>(entity.Status),
            ParseSchema(entity.ConfigurationSchema),
            entity.ConfigurationSchemaVersion == 0 ? 1 : entity.ConfigurationSchemaVersion,
            ParseDefaults(entity.DefaultConfiguration));
    }

    private static CapabilityEntity ToEntity(Capability capability)
    {
        return new CapabilityEntity
        {
            RowKey = capability.CapabilityId,
            CapabilityName = capability.Name,
            CapabilityKey = capability.Key!,
            CapabilityType = capability.CapabilityType.ToString(),
            Status = capability.Status.ToString(),
            ConfigurationSchema = SerializeSchema(capability.ConfigurationSchema),
            ConfigurationSchemaVersion = capability.ConfigurationSchemaVersion,
            DefaultConfiguration = SerializeDefaults(capability.DefaultConfiguration)
        };
    }

    private static CapabilityAdminDto ToDto(CapabilityEntity entity)
    {
        return new CapabilityAdminDto(
            Guid.Parse(entity.RowKey),
            entity.CapabilityKey,
            entity.CapabilityName,
            entity.CapabilityType,
            string.IsNullOrWhiteSpace(entity.Status) ? CapabilityStatus.Active.ToString() : entity.Status,
            ParseSchema(entity.ConfigurationSchema).Select(FieldToDto).ToList(),
            entity.ConfigurationSchemaVersion == 0 ? 1 : entity.ConfigurationSchemaVersion,
            ParseDefaults(entity.DefaultConfiguration));
    }

    private static IReadOnlyList<CapabilityConfigurationField> ToDomainSchema(
        IReadOnlyList<CapabilityConfigurationFieldDto>? schema)
    {
        if (schema == null || schema.Count == 0)
            return EmptySchema;

        return schema.Select(DtoToField).ToList();
    }

    private static CapabilityConfigurationField DtoToField(CapabilityConfigurationFieldDto dto)
    {
        return new CapabilityConfigurationField(
            dto.Name,
            Enum.Parse<CapabilityConfigurationFieldType>(dto.Type),
            dto.Required,
            dto.Minimum,
            dto.Maximum,
            dto.AllowedValues,
            dto.DefaultValue);
    }

    private static CapabilityConfigurationFieldDto FieldToDto(CapabilityConfigurationField field)
    {
        return new CapabilityConfigurationFieldDto(
            field.Name,
            field.Type.ToString(),
            field.Required,
            field.Minimum,
            field.Maximum,
            field.AllowedValues,
            field.DefaultValue);
    }

    private static string? SerializeSchema(IReadOnlyList<CapabilityConfigurationField> schema)
    {
        if (schema.Count == 0)
            return null;

        return System.Text.Json.JsonSerializer.Serialize(schema.Select(FieldToDto).ToList());
    }

    private static IReadOnlyList<CapabilityConfigurationField> ParseSchema(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
            return EmptySchema;

        var dtos = System.Text.Json.JsonSerializer.Deserialize<List<CapabilityConfigurationFieldDto>>(schema);

        return dtos == null || dtos.Count == 0 ? EmptySchema : dtos.Select(DtoToField).ToList();
    }

    private static string? SerializeDefaults(IReadOnlyDictionary<string, string> defaults)
    {
        if (defaults.Count == 0)
            return null;

        return System.Text.Json.JsonSerializer.Serialize(defaults);
    }

    private static IReadOnlyDictionary<string, string> ParseDefaults(string? defaults)
    {
        if (string.IsNullOrWhiteSpace(defaults))
            return EmptyDefaults;

        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(defaults)
            ?? new Dictionary<string, string>();
    }
}
