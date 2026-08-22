namespace Vivnest.Runtime.Capabilities;

public interface IRuntimeCapabilityAssignmentStore
{
    IReadOnlyCollection<RuntimeCapabilityAssignment> GetAll();

    IReadOnlyCollection<RuntimeCapabilityAssignment> GetEnabled();

    RuntimeCapabilityAssignment? Get(string capabilityId);
}
