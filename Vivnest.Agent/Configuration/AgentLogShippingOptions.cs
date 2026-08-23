using Microsoft.Extensions.Logging;

namespace Vivnest.Agent.Configuration;

// Warning+Error only by default, deliberately - several workers already
// log at Info level every tick, and shipping all of that would be a lot
// of blob churn for mostly noise. This only controls what the *shipped*
// buffer captures; the existing Console provider (and its own
// Logging:LogLevel config) is untouched, so local/docker-log verbosity
// doesn't change.
public sealed class AgentLogShippingOptions
{
    public bool Enabled { get; set; } = true;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Warning;

    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMinutes(5);

    // See AgentMetricsOptions.EffectiveInterval.
    public TimeSpan EffectiveFlushInterval =>
        FlushInterval > TimeSpan.Zero ? FlushInterval : TimeSpan.FromMinutes(5);

    // Ring buffer, not unbounded - oldest lines drop first. "Download
    // recent problems" is the actual use case, not a full history.
    public int MaxBufferedLines { get; set; } = 500;
}
