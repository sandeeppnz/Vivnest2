namespace Vivnest.Core.Capabilities;

public interface IRuntimeCapabilityAssignmentStore
{
    IReadOnlyCollection<RuntimeCapabilityAssignment> GetAll();

    IReadOnlyCollection<RuntimeCapabilityAssignment> GetEnabled();

    RuntimeCapabilityAssignment? Get(string capabilityId);
}
