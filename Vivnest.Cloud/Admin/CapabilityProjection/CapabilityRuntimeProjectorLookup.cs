namespace Vivnest.Cloud.Admin.CapabilityProjection;

// Shared capability-name matching between Device and Agent Configuration
// Projection (decision-log.md ADR-064) - same case/whitespace-insensitive
// convention DeviceRuntimeConfigurationProjector.MatchRuntimeDeviceType
// already uses for DeviceType, since Capability.Name is free-text
// admin-typed master data too, not a fixed enum.
internal static class CapabilityRuntimeProjectorLookup
{
    public static ICapabilityRuntimeProjector? Find(
        IEnumerable<ICapabilityRuntimeProjector> projectors, string capabilityName)
    {
        var normalized = capabilityName.Replace(" ", "");

        return projectors.FirstOrDefault(p =>
            string.Equals(
                p.CapabilityName.Replace(" ", ""), normalized, StringComparison.OrdinalIgnoreCase));
    }
}
