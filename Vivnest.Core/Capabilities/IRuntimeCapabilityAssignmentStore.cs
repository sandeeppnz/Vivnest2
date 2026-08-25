namespace Vivnest.Core.Capabilities;

// One member, deliberately. GetAll() and Get(id) sat here with zero
// production callers - CapabilityHost, the sole consumer, only ever asks
// for the enabled set - and every implementation (test stubs included) was
// forced to carry them anyway. Add a member back when a caller exists,
// not before.
public interface IRuntimeCapabilityAssignmentStore
{
    IReadOnlyCollection<RuntimeCapabilityAssignment> GetEnabled();
}
