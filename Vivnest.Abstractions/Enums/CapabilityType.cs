namespace Vivnest.Abstractions.Enums;

// Canonical classification for a master Capability record (Admin >
// Capabilities) - who provides it, not how it's computed (that framing
// - BuiltIn/Derived - turned out ambiguous in practice: real capabilities
// like Motion Detection got classified inconsistently depending on
// whether you thought about the sensor's own firmware or the logical
// event). Device = the device itself provides it (Image Capture, Motion
// Detection, Power Monitoring). Service = a separate process computes it
// from something else the device produced (Object Detection, Image
// Classification). System = the platform, not the device or a service
// (Health Monitoring). Deliberately a separate vocabulary from the
// existing per-device Source strings ("Built-in"/"Derived"/"System") in
// DeviceCapabilitiesDto - unifying the two is future work, out of scope here.
public enum CapabilityType
{
    Device,
    Service,
    System
}
