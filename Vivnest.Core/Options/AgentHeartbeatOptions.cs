namespace Vivnest.Core.Options;

public class AgentHeartbeatOptions
{
    public bool Enabled { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }
}
