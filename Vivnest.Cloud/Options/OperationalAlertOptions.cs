namespace Vivnest.Cloud.Options;

// Sprint 8. Both limits exist because they fail differently, and the
// roadmap flagged getting this wrong as the thing blocking the feature.
//
// CooldownPerSignature stops the same fault repeating - a crash-looping
// worker logging the identical error hundreds of times a minute (this
// codebase has hit that for real twice: ADR-023's RTSP timeout and
// ADR-024's restart-policy incident). Keying on the signature rather than
// on the agent is what stops a noisy subsystem masking a genuinely
// different fault on the same agent.
//
// MaxNotificationsPerAgentPerHour is the backstop for the case the
// cooldown cannot catch: many DISTINCT errors at once, where every one is
// a new signature and so every one is allowed through. Without it, an
// agent failing in a novel way each second still floods.
public sealed class OperationalAlertOptions
{
    // Off by default: turning this on starts sending notifications, and
    // that should be a deliberate act, not something a deploy does.
    public bool Enabled { get; set; }

    public TimeSpan CooldownPerSignature { get; set; } = TimeSpan.FromMinutes(10);

    public int MaxNotificationsPerAgentPerHour { get; set; } = 12;
}
