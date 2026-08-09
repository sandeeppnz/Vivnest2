using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Admin;

// A genuine hard delete, unlike ApiKeyManagementService.RevokeAsync's
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
        var entity = new CapabilityEntity
        {
            RowKey = Guid.NewGuid().ToString(),
            CapabilityName = capabilityName,
            CapabilityType = capabilityType
        };

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

        entity.CapabilityName = capabilityName;
        entity.CapabilityType = capabilityType;

        await _capabilities.UpdateAsync(entity, cancellationToken);

        return ToDto(entity);
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

    private static CapabilityAdminDto ToDto(CapabilityEntity entity)
    {
        return new CapabilityAdminDto(
            Guid.Parse(entity.RowKey),
            entity.CapabilityName,
            entity.CapabilityType);
    }
}
