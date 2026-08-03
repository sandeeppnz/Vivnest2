using Vivnest.Core.Options;

namespace Vivnest.Agent.Services;

public interface ISinkCleanlinessAnalyzer
{
    SinkCleanlinessResult Analyze(byte[] imageBytes, SinkCleanlinessOptions options);
}

public sealed class SinkCleanlinessResult
{
    public required bool Clean { get; init; }
    public required double Score { get; init; }
}
