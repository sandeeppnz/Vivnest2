namespace Vivnest.Abstraction.Agent.Capabilities;

public interface IRuntimeCapabilityAssignmentStore
{
    IReadOnlyCollection<RuntimeCapabilityAssignment> GetAll();

    IReadOnlyCollection<RuntimeCapabilityAssignment> GetEnabled();

    RuntimeCapabilityAssignment? Get(string capabilityId);
}
