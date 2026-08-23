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

    // The error-signal buffer's cap, which ErrorEventWorker drains into
    // AgentEvent rows. Configurable for the same reason MaxBufferedLines
    // is; it was the one buffer size hardcoded at its call site.
    //
    // Lives under AgentLogShipping despite being about error events rather
    // than shipped logs, because the same logger provider fills both and
    // this section already configures that provider. Note the error buffer
    // stays active even when Enabled is false - see AgentLogShipping's
    // registration.
    public int MaxBufferedErrorSignals { get; set; } = 200;
}
