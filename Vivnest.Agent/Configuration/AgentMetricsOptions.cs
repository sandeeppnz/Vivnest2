namespace Vivnest.Agent.Configuration;

public class AgentMetricsOptions
{
    public bool Enabled { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    // Has a default, so only an explicit zero can reach PeriodicTimer -
    // floored anyway, on the same grounds as the two heartbeat intervals.
    public TimeSpan EffectiveInterval =>
        Interval > TimeSpan.Zero ? Interval : TimeSpan.FromMinutes(1);
}
