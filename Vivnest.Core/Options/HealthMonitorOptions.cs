namespace Vivnest.Core.Options;

public class HealthMonitorOptions
{
    public bool Enabled { get; set; } = true;
    public string CronSchedule { get; set; } = "0 */5 * * * *";

    // Agent is considered stale once its last heartbeat is older than
    // HeartbeatInterval * AgentStaleMultiplier.
    public int AgentStaleMultiplier { get; set; } = 3;
}
