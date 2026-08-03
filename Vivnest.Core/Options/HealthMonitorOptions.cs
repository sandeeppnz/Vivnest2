namespace Vivnest.Core.Options;

public class HealthMonitorOptions
{
    public bool Enabled { get; set; } = true;
    public string CronSchedule { get; set; } = "0 */5 * * * *";

    // Agent is considered stale once its last heartbeat is older than
    // HeartbeatInterval * AgentStaleMultiplier.
    public int AgentStaleMultiplier { get; set; } = 3;

    // Home Assistant-sourced devices are considered stale once the agent's
    // WebSocket connection to HA has been unconfirmed for longer than this -
    // a flat duration, not a multiplier, since there's no natural "HA
    // heartbeat interval" to multiply (the connection is either continuously
    // fed by real traffic or it's reconnecting on a fixed 10s delay).
    public TimeSpan HomeAssistantConnectionStaleAfter { get; set; } = TimeSpan.FromMinutes(3);
}
