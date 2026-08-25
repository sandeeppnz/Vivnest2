using Vivnest.Core.Capabilities;

namespace Vivnest.Runtime.Capabilities;

public sealed class CapabilityRegistry
    : ICapabilityRegistry
{
    private readonly Dictionary<string, ICapability> _capabilities =
        new(StringComparer.OrdinalIgnoreCase);

    public CapabilityRegistry(
        IEnumerable<ICapability> capabilities)
    {
        foreach (var capability in capabilities)
        {
            Register(capability);
        }
    }

    public IReadOnlyCollection<ICapability> GetAll()
    {
        return _capabilities.Values.ToArray();
    }

    public ICapability? Get(
        string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
        {
            return null;
        }

        return _capabilities.TryGetValue(
            capabilityId,
            out var capability)
                ? capability
                : null;
    }

    private void Register(
        ICapability capability)
    {
        ArgumentNullException.ThrowIfNull(
            capability);

        var id = capability.Manifest.Id;

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException(
                "Capability manifest must contain an Id.");
        }

        if (_capabilities.ContainsKey(id))
        {
            throw new InvalidOperationException(
                $"Capability '{id}' is already registered.");
        }

        _capabilities.Add(
            id,
            capability);
    }
}