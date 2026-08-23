using Vivnest.Core.Capabilities;

namespace Vivnest.Runtime.Capabilities;

public interface ICapabilityRegistry
{
    IReadOnlyCollection<ICapability> GetAll();

    ICapability? Get(string capabilityId);

    void Register(ICapability capability);
}
