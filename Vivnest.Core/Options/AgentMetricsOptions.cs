namespace Vivnest.Core.Options;

public class AgentMetricsOptions
{
    public bool Enabled { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}
