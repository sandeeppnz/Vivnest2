namespace Vivnest.Core.Options;

// Moved from Vivnest.Agent.Configuration (ADR-120) so SharedConfigPublisher
// can serialize the same instance the Agent binds - one source of truth for
// the published defaults.
public class AgentMetricsOptions
{
    // On by default (ADR-120), same grounds as the heartbeat options.
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    // Has a default, so only an explicit zero can reach PeriodicTimer -
    // floored anyway, on the same grounds as the two heartbeat intervals.
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan EffectiveInterval =>
        Interval > TimeSpan.Zero ? Interval : TimeSpan.FromMinutes(1);
}
