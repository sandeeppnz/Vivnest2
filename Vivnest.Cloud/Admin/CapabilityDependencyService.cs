using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;

namespace Vivnest.Cloud.Admin;

// Orchestrates the Capability dependency graph (decision-log.md ADR-062,
// Phase 5) - belongs here, not in AzureTableCapabilityDependencyStore,
// same "orchestration lives in the management service" split every other
// admin feature in this codebase uses.
public sealed class CapabilityDependencyService : ICapabilityDependencyService
{
    private readonly ICapabilityDependencyStore _dependencies;
    private readonly ICapabilityStore _capabilities;

    public CapabilityDependencyService(
        ICapabilityDependencyStore dependencies,
        ICapabilityStore capabilities)
    {
        _dependencies = dependencies;
        _capabilities = capabilities;
    }

    public async Task<IReadOnlyList<CapabilityDependencyDto>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        var entities = await _dependencies.ListAsync(cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<CapabilityDependencyResult> AddAsync(
        string capabilityId,
        string dependsOnCapabilityId,
        CancellationToken cancellationToken = default)
    {
        if (capabilityId == dependsOnCapabilityId)
        {
            return new CapabilityDependencyResult(
                null, CapabilityDependencyError.SelfReference, "A capability cannot depend on itself.");
        }

        var capability = await _capabilities.GetAsync(capabilityId, cancellationToken);

        if (capability == null)
        {
            return new CapabilityDependencyResult(
                null, CapabilityDependencyError.CapabilityNotFound, $"CapabilityId \"{capabilityId}\" doesn't exist.");
        }

        var dependsOnCapability = await _capabilities.GetAsync(dependsOnCapabilityId, cancellationToken);

        if (dependsOnCapability == null)
        {
            return new CapabilityDependencyResult(
                null,
                CapabilityDependencyError.DependsOnCapabilityNotFound,
                $"DependsOnCapabilityId \"{dependsOnCapabilityId}\" doesn't exist.");
        }

        var all = await _dependencies.ListAsync(cancellationToken);

        if (all.Any(d => d.CapabilityId == capabilityId && d.DependsOnCapabilityId == dependsOnCapabilityId))
        {
            return new CapabilityDependencyResult(
                null, CapabilityDependencyError.AlreadyExists, "This dependency already exists.");
        }

        if (WouldCreateCycle(all, capabilityId, dependsOnCapabilityId))
        {
            return new CapabilityDependencyResult(
                null,
                CapabilityDependencyError.CircularDependency,
                "Capability dependency would create a circular dependency.");
        }

        var dependency = new CapabilityDependency(capabilityId, dependsOnCapabilityId);
        var entity = ToEntity(dependency);

        await _dependencies.CreateAsync(entity, cancellationToken);

        return new CapabilityDependencyResult(ToDto(entity), null, null);
    }

    public async Task<bool> RemoveAsync(
        string dependencyId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dependencies.GetAsync(dependencyId, cancellationToken);

        if (entity == null)
            return false;

        await _dependencies.DeleteAsync(dependencyId, cancellationToken);

        return true;
    }

    // Adding edge (capabilityId -> dependsOnCapabilityId) closes a cycle
    // if dependsOnCapabilityId can already reach capabilityId by
    // following existing "requires" edges forward - that reachable path
    // plus the new edge is the cycle.
    private static bool WouldCreateCycle(
        IReadOnlyList<CapabilityDependencyEntity> existing,
        string capabilityId,
        string dependsOnCapabilityId)
    {
        var adjacency = existing
            .GroupBy(d => d.CapabilityId)
            .ToDictionary(g => g.Key, g => g.Select(d => d.DependsOnCapabilityId).ToList());

        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(dependsOnCapabilityId);
        visited.Add(dependsOnCapabilityId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current == capabilityId)
                return true;

            if (!adjacency.TryGetValue(current, out var next))
                continue;

            foreach (var neighbor in next)
            {
                if (visited.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    private static CapabilityDependencyEntity ToEntity(CapabilityDependency dependency)
    {
        return new CapabilityDependencyEntity
        {
            RowKey = dependency.DependencyId,
            CapabilityId = dependency.CapabilityId,
            DependsOnCapabilityId = dependency.DependsOnCapabilityId,
            DependencyType = dependency.DependencyType.ToString()
        };
    }

    private static CapabilityDependencyDto ToDto(CapabilityDependencyEntity entity)
    {
        return new CapabilityDependencyDto(
            Guid.Parse(entity.RowKey),
            Guid.Parse(entity.CapabilityId),
            Guid.Parse(entity.DependsOnCapabilityId),
            entity.DependencyType);
    }
}
