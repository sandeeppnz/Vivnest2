namespace Vivnest.Core.Configuration;

// Object Detection's ROI configuration. The translation itself lives in
// RoiCapabilityRuntimeAdapter (ADR-108) - shared with Sink Cleanliness,
// which does the identical thing to a different sub-object.
public sealed class ObjectDetectionRuntimeAdapter : RoiCapabilityRuntimeAdapter
{
    public override string CapabilityName => "Object Detection";

    protected override string DeviceOptionsKey => "ObjectDetection";
}
