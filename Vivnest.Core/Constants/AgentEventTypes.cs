namespace Vivnest.Core.Constants;

public static class AgentEventTypes
{
    public const string MetricsReported = "MetricsReported";

    // Audit trail for IAgentRuntimeConfigurationPublisher (decision-log.md
    // ADR-064) - fires whenever an Agent's "aiClassification" section is
    // actually written to agent-config/{agentId}.json.
    public const string ConfigPublished = "ConfigPublished";

    // Decision-log.md ADR-070 - see DeviceEventTypes.ConfigRolledBack, same
    // reasoning, mirrored here.
    public const string ConfigRolledBack = "ConfigRolledBack";

    // Decision-log.md ADR-077 (Phase 8 Pass 4) - persisted alongside the
    // existing Telegram notification HealthMonitorService already sends
    // (NotificationTypes.AgentOffline/AgentRecovered), not a replacement
    // for it - these give the same transition a permanent row in this
    // Agent's own event history, gated by the identical NotificationState
    // machine so they fire exactly once per real transition.
    public const string AgentOffline = "AgentOffline";
    public const string AgentRecovered = "AgentRecovered";

    // Sprint 8 - an Error-level log call from anywhere in the Agent
    // process becomes a real event row, so the operational failures that
    // previously only reached agent-logs/{agentId}.txt (ADR-027, read by a
    // human who thought to look) now flow through the same
    // event -> queue -> notification path everything else uses.
    public const string ErrorLogged = "ErrorLogged";

    // Decision-log.md ADR-077 - fires when this Agent's own
    // ConfigurationLoadError transitions from unset to set, gated by the
    // new LastNotifiedConfigurationLoadError field so a persistent error
    // doesn't re-fire on every health-check tick.
    public const string ConfigurationApplyFailed = "ConfigurationApplyFailed";
}
