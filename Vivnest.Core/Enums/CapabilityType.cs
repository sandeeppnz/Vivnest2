namespace Vivnest.Core.Enums;

// Canonical classification for a master Capability record (Admin >
// Capabilities). Deliberately a separate vocabulary from the existing
// per-device Source strings ("Built-in"/"Derived"/"System") in
// DeviceCapabilitiesDto - unifying the two is future work, out of scope here.
public enum CapabilityType
{
    BuiltIn,
    Derived,
    System
}
