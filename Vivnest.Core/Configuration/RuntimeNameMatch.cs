using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;

namespace Vivnest.Core.Configuration;

// Admin master data (Capability.CapabilityName, DeviceType.DeviceTypeName)
// is free text - "Motion Sensor", "Sink Cleanliness" - while the code it
// binds to uses identifier spelling (DeviceType.MotionSensor,
// SinkCleanlinessRuntimeAdapter.CapabilityName). The bridge is the same
// rule everywhere: strip spaces, compare OrdinalIgnoreCase.
//
// That rule was written out four separate times before this existed -
// CapabilityRuntimeProjectorLookup (Cloud), CapabilityConfigRuntimeAdapterLookup
// (Core), DeviceRuntimeConfigurationProjector.MatchRuntimeDeviceType and
// MotionDetectionRuntimeProjector's own copy of the same DeviceType match.
// All four agreed, and their comments openly said so ("mirrors ... exactly",
// "same convention ... already uses"), which is precisely the shape of
// duplication that drifts silently: the failure mode isn't an exception,
// it's a capability quietly dropping out of a published document with only
// a warning.
//
// Note this rule is deliberately forgiving in one direction only. It
// normalizes whitespace and case; it does NOT normalize punctuation,
// pluralization or spelling. Renaming a Capability in Admin to anything
// that isn't just a respacing/recasing still unbinds it from its
// projector - see the 2026-08 dead-code audit's U-D4/L-series notes.
public static class RuntimeNameMatch
{
    public static string Normalize(string name) => name.Replace(" ", "");

    public static bool Matches(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static T? Find<T>(IEnumerable<T> candidates, string name, Func<T, string> nameOf)
        where T : class =>
        candidates.FirstOrDefault(candidate => Matches(nameOf(candidate), name));

    // Matches against Enum.GetNames rather than Enum.TryParse on purpose:
    // TryParse also accepts the underlying numeric value, so an admin
    // DeviceTypeName of "0" would silently resolve to Camera. Both original
    // call sites used GetNames; this preserves that exactly.
    public static DeviceType? ToDeviceType(string deviceTypeName)
    {
        var normalized = Normalize(deviceTypeName);

        var match = Enum.GetNames<DeviceType>()
            .FirstOrDefault(n => string.Equals(n, normalized, StringComparison.OrdinalIgnoreCase));

        return match == null ? null : Enum.Parse<DeviceType>(match);
    }
}
