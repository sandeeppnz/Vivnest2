namespace Vivnest.Core.Configuration;

// Mirrors Vivnest.Cloud's CapabilityRuntimeProjectorLookup exactly - same
// case/whitespace-insensitive match, since the capability name traveling
// on the wire is the same free-text Capability.CapabilityName the Cloud
// side already matched this way.
internal static class CapabilityConfigRuntimeAdapterLookup
{
    public static ICapabilityConfigRuntimeAdapter? Find(
        IEnumerable<ICapabilityConfigRuntimeAdapter> adapters, string capabilityName)
    {
        var normalized = capabilityName.Replace(" ", "");

        return adapters.FirstOrDefault(a =>
            string.Equals(
                a.CapabilityName.Replace(" ", ""), normalized, StringComparison.OrdinalIgnoreCase));
    }
}
