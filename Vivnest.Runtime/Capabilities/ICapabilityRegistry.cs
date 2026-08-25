using Vivnest.Core.Capabilities;

namespace Vivnest.Runtime.Capabilities;

// Read-only on purpose. Register lived on this interface too, inviting a
// capability to be added AFTER CapabilityHost's startup pass - which would
// register something the host never starts and never pairs with an
// assignment. Registration happens exactly once, in the concrete
// registry's constructor, from DI; the interface exposes only reads.
public interface ICapabilityRegistry
{
    IReadOnlyCollection<ICapability> GetAll();

    ICapability? Get(string capabilityId);
}
