using Vivnest.Core.Options;

namespace Vivnest.Capabilities.AiClassification.Inference;

public interface ISinkCleanlinessClassifier
{
    // Null means "couldn't classify" (model missing/failed to load, image
    // undecodable) - callers treat that as "skip this capture", not as dirty
    // or clean, since neither would be a real observation.
    //
    // A non-null result below the caller's ConfidenceThreshold is skipped on
    // the same grounds. That is the caller's rule, not this method's: the
    // classifier reports what it saw and how sure it is, and does not decide
    // how sure is sure enough.
    SinkCleanlinessResult? Classify(byte[] imageBytes, SinkCleanlinessOptions options);
}

public sealed class SinkCleanlinessResult
{
    public required bool IsClean { get; init; }
    public required double Confidence { get; init; }
}
