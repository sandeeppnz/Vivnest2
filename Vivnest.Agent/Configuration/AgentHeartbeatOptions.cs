namespace Vivnest.Agent.Configuration;

public class AgentHeartbeatOptions
{
    public bool Enabled { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }

    // Floored at the point of use: an absent setting binds to TimeSpan.Zero,
    // and PeriodicTimer(TimeSpan.Zero) throws ArgumentOutOfRangeException,
    // which faults the BackgroundService and stops the whole host on .NET 8.
    // Deliberately NOT ValidateOnStart: an Agent that refuses to boot tells
    // Cloud nothing at all, it simply appears offline. Better to run at a
    // sane cadence and report the misconfiguration over the channel that
    // still works.
    public TimeSpan EffectiveHeartbeatInterval =>
        HeartbeatInterval > TimeSpan.Zero
            ? HeartbeatInterval
            : TimeSpan.FromMinutes(1);
}
