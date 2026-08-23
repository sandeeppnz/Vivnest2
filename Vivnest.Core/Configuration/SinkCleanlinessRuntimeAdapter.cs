namespace Vivnest.Core.Configuration;

// Sink Cleanliness's ROI configuration. The translation itself lives in
// RoiCapabilityRuntimeAdapter (ADR-108) - shared with Object Detection,
// which does the identical thing to a different sub-object.
public sealed class SinkCleanlinessRuntimeAdapter : RoiCapabilityRuntimeAdapter
{
    public override string CapabilityName => "Sink Cleanliness";

    protected override string DeviceOptionsKey => "SinkCleanliness";
}
