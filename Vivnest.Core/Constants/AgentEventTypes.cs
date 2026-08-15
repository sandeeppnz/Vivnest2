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

    // Decision-log.md ADR-077 - fires when this Agent's own
    // ConfigurationLoadError transitions from unset to set, gated by the
    // new LastNotifiedConfigurationLoadError field so a persistent error
    // doesn't re-fire on every health-check tick.
    public const string ConfigurationApplyFailed = "ConfigurationApplyFailed";
}
