namespace Vivnest.Cloud.Options;

public class HealthMonitorOptions
{
    public bool Enabled { get; set; } = true;
    public string CronSchedule { get; set; } = "0 */5 * * * *";

    // Agent is considered stale once its last heartbeat is older than
    // HeartbeatInterval * AgentStaleMultiplier. Used only by
    // DeviceStatusResolver to decide when a *device's* status should
    // cascade to Offline because its owning agent is stale - a
    // deliberately different question from the agent's own health below,
    // per DeviceStatusResolver's own comment.
    public int AgentStaleMultiplier { get; set; } = 3;

    // Decision-log.md ADR-074 - the agent's own tiered health
    // (IAgentStatusResolver): Online up to Degraded x, Warning up to
    // Offline x, Offline beyond that. Deliberately separate from
    // AgentStaleMultiplier above - this drives the agent's own
    // Online/Warning/Offline/Unknown status and its offline notification,
    // not the device cascade.
    public int AgentDegradedMultiplier { get; set; } = 2;
    public int AgentOfflineMultiplier { get; set; } = 5;

    // Home Assistant-sourced devices are considered stale once the agent's
    // WebSocket connection to HA has been unconfirmed for longer than this -
    // a flat duration, not a multiplier, since there's no natural "HA
    // heartbeat interval" to multiply (the connection is either continuously
    // fed by real traffic or it's reconnecting on a fixed 10s delay).
    public TimeSpan HomeAssistantConnectionStaleAfter { get; set; } = TimeSpan.FromMinutes(3);
}
