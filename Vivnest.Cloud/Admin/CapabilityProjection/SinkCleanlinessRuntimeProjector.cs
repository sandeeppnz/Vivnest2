namespace Vivnest.Cloud.Admin.CapabilityProjection;

// Sink Cleanliness's Cloud-side projection. The ROI/model translation
// lives in RoiCapabilityRuntimeProjector (ADR-108), shared with Object
// Detection. Sink Cleanliness adds no optional agent settings - a
// classifier with one output needs no ExpectedClasses.
public sealed class SinkCleanlinessRuntimeProjector : RoiCapabilityRuntimeProjector
{
    public override string CapabilityName => "Sink Cleanliness";
}
