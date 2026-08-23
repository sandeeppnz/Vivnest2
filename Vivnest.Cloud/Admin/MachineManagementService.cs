using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Machines;
using Vivnest.Domain.Sites;

namespace Vivnest.Cloud.Admin;

// Maps the persistence-agnostic Machine domain model (Vivnest.Core.Domain)
// to/from MachineEntity for storage - same shape as
// AgentRegistryManagementService (generated-Guid id, no collision
// handling needed on create). No hard delete - a Machine's identity
// should remain stable for its lifetime (decision-log.md ADR-053);
// retire it via UpdateAsync(status: Retired/Decommissioned) instead of
// removing the row.
public sealed class MachineManagementService : IMachineManagementService
{
    private readonly IMachineStore _machines;

    public MachineManagementService(IMachineStore machines)
    {
        _machines = machines;
    }

    public async Task<IReadOnlyList<MachineDto>> ListAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var entities = await _machines.ListAsync(tenant.TenantId, tenant.SiteId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<MachineDto?> GetAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _machines.GetAsync(tenant.TenantId, tenant.SiteId, machineId, cancellationToken);

        return entity == null ? null : ToDto(entity);
    }

    public async Task<MachineDto> CreateAsync(
        TenantContext tenant,
        string name,
        string? hostname,
        string? description,
        string? operatingSystem,
        string? architecture,
        CancellationToken cancellationToken = default)
    {
        var machine = new Machine(
            tenant.TenantId,
            tenant.SiteId,
            name,
            hostname,
            description,
            operatingSystem,
            architecture);

        var entity = ToEntity(machine);

        await _machines.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<MachineDto?> UpdateAsync(
        TenantContext tenant,
        string machineId,
        string name,
        string? hostname,
        string? description,
        string status,
        string? operatingSystem,
        string? architecture,
        CancellationToken cancellationToken = default)
    {
        var entity = await _machines.GetAsync(tenant.TenantId, tenant.SiteId, machineId, cancellationToken);

        if (entity == null)
            return null;

        var machine = ToDomain(entity);
        machine.Update(name, hostname, description, operatingSystem, architecture);
        machine.SetStatus(Enum.Parse<MachineStatus>(status));

        var updated = ToEntity(machine);
        updated.ETag = entity.ETag;

        await _machines.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    private static Machine ToDomain(MachineEntity entity)
    {
        return Machine.Rehydrate(
            entity.TenantId,
            entity.SiteId,
            entity.RowKey,
            entity.Name,
            entity.Hostname,
            entity.Description,
            Enum.Parse<MachineStatus>(entity.Status),
            entity.OperatingSystem,
            entity.Architecture,
            entity.CreatedUtc,
            entity.UpdatedUtc);
    }

    private static MachineEntity ToEntity(Machine machine)
    {
        return new MachineEntity
        {
            PartitionKey = new SiteScope(machine.TenantId, machine.SiteId).PartitionKey,
            RowKey = machine.MachineId,
            TenantId = machine.TenantId,
            SiteId = machine.SiteId,
            Name = machine.Name,
            Hostname = machine.Hostname,
            Description = machine.Description,
            Status = machine.Status.ToString(),
            OperatingSystem = machine.OperatingSystem,
            Architecture = machine.Architecture,
            CreatedUtc = machine.CreatedUtc,
            UpdatedUtc = machine.UpdatedUtc
        };
    }

    private static MachineDto ToDto(MachineEntity entity)
    {
        return new MachineDto(
            Guid.Parse(entity.RowKey),
            entity.Name,
            entity.Hostname,
            entity.Description,
            entity.Status,
            entity.OperatingSystem,
            entity.Architecture,
            entity.CreatedUtc,
            entity.UpdatedUtc,
            entity.TenantId,
            entity.SiteId);
    }
}
