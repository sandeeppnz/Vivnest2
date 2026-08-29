namespace Vivnest.Core.Options;

// Moved from Vivnest.Agent.Configuration (ADR-120) so SharedConfigPublisher
// can serialize the same instance the Agent binds - one source of truth for
// the published defaults.
public class AgentHeartbeatOptions
{
    // On by default (ADR-120): heartbeats are how Cloud decides agent
    // health, so "config section missing" silently meaning "no
    // heartbeats" was a trap, proven by the 2026-08-29 rebuild. Config
    // can still turn it off explicitly.
    public bool Enabled { get; set; } = true;
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);

    // Floored at the point of use: an explicit zero still binds, and
    // PeriodicTimer(TimeSpan.Zero) throws ArgumentOutOfRangeException,
    // which faults the BackgroundService and stops the whole host on .NET 8.
    // Deliberately NOT ValidateOnStart: an Agent that refuses to boot tells
    // Cloud nothing at all, it simply appears offline. Better to run at a
    // sane cadence and report the misconfiguration over the channel that
    // still works.
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan EffectiveHeartbeatInterval =>
        HeartbeatInterval > TimeSpan.Zero
            ? HeartbeatInterval
            : TimeSpan.FromMinutes(1);
}
