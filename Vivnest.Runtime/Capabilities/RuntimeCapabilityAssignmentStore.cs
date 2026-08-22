namespace Vivnest.Runtime.Capabilities;
using Vivnest.Abstraction.Agent.Capabilities;

public sealed class RuntimeCapabilityAssignmentStore : IRuntimeCapabilityAssignmentStore
{
    private readonly IReadOnlyList<RuntimeCapabilityAssignment>
        _assignments;

    public RuntimeCapabilityAssignmentStore(
        IEnumerable<RuntimeCapabilityAssignment> assignments)
    {
        _assignments = assignments.ToList();
    }

    public IReadOnlyCollection<RuntimeCapabilityAssignment>
        GetAll()
    {
        return _assignments;
    }

    public IReadOnlyCollection<RuntimeCapabilityAssignment>
        GetEnabled()
    {
        return _assignments
            .Where(x => x.Enabled)
            .ToList();
    }

    public RuntimeCapabilityAssignment? Get(
        string capabilityId)
    {
        return _assignments.FirstOrDefault(
            x => string.Equals(
                x.CapabilityId,
                capabilityId,
                StringComparison.OrdinalIgnoreCase));
    }
}
