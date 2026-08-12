using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

// Maps the persistence-agnostic Capability domain model (Vivnest.Core.Domain)
// to/from CapabilityEntity for storage (decision-log.md ADR-057). A
// genuine hard delete, unlike ApiKeyManagementService.RevokeAsync's
// Enabled=false soft-delete - Capability is master/reference data meant to
// actually shrink, not an audit trail. CapabilityType string validation
// (Enum.TryParse) happens in the Function layer before calling here - this
// service trusts already-validated input, matching ApiKeysFunction's
// validate-then-call-service pattern.
public sealed class CapabilityManagementService : ICapabilityManagementService
{
    private readonly ICapabilityStore _capabilities;

    public CapabilityManagementService(ICapabilityStore capabilities)
    {
        _capabilities = capabilities;
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
        CancellationToken cancellationToken = default)
    {
        var capability = new Capability(capabilityName, Enum.Parse<CapabilityType>(capabilityType));

        var entity = ToEntity(capability);

        await _capabilities.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<CapabilityAdminDto?> UpdateAsync(
        string capabilityId,
        string capabilityName,
        string capabilityType,
        CancellationToken cancellationToken = default)
    {
        var entity = await _capabilities.GetAsync(capabilityId, cancellationToken);

        if (entity == null)
            return null;

        var capability = ToDomain(entity);
        capability.Update(capabilityName, Enum.Parse<CapabilityType>(capabilityType));

        var updated = ToEntity(capability);
        updated.ETag = entity.ETag;

        await _capabilities.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    public async Task<bool> DeleteAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _capabilities.GetAsync(capabilityId, cancellationToken);

        if (entity == null)
            return false;

        await _capabilities.DeleteAsync(capabilityId, cancellationToken);

        return true;
    }

    private static Capability ToDomain(CapabilityEntity entity)
    {
        return Capability.Rehydrate(
            entity.RowKey,
            entity.CapabilityName,
            Enum.Parse<CapabilityType>(entity.CapabilityType));
    }

    private static CapabilityEntity ToEntity(Capability capability)
    {
        return new CapabilityEntity
        {
            RowKey = capability.CapabilityId,
            CapabilityName = capability.Name,
            CapabilityType = capability.CapabilityType.ToString()
        };
    }

    private static CapabilityAdminDto ToDto(CapabilityEntity entity)
    {
        return new CapabilityAdminDto(
            Guid.Parse(entity.RowKey),
            entity.CapabilityName,
            entity.CapabilityType);
    }
}
