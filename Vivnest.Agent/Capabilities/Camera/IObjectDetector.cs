using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Camera;

public interface IObjectDetector
{
    // Empty list means either "couldn't run" (model missing/failed to
    // load, image undecodable) or "ran fine, found nothing" - both
    // consumers here (person-gate, unusual-object flag) treat those
    // identically, so the distinction isn't surfaced in the return value.
    IReadOnlyList<Detection> Detect(byte[] imageBytes, ObjectDetectionOptions options);
}

public sealed class Detection
{
    public required string ClassName { get; init; }
    public required float Confidence { get; init; }

    // Pixel coordinates in the original capture's own resolution, not the
    // model's input size - ObjectDetector scales boxes back before
    // returning them.
    public required int X1 { get; init; }
    public required int Y1 { get; init; }
    public required int X2 { get; init; }
    public required int Y2 { get; init; }
}
