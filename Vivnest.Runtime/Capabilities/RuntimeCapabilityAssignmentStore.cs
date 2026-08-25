using Vivnest.Core.Capabilities;

namespace Vivnest.Runtime.Capabilities;

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
        GetEnabled()
    {
        return _assignments
            .Where(x => x.Enabled)
            .ToList();
    }
}
