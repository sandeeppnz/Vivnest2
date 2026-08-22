using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Camera;

public interface ISinkCleanlinessClassifier
{
    // Null means "couldn't classify" (model missing/failed to load, image
    // undecodable) - callers treat that as "skip this capture", not as dirty
    // or clean, since neither would be a real observation.
    SinkCleanlinessResult? Classify(byte[] imageBytes, SinkCleanlinessOptions options);
}

public sealed class SinkCleanlinessResult
{
    public required bool IsClean { get; init; }
    public required double Confidence { get; init; }
}
