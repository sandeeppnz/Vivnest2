namespace Vivnest.Cloud.Options;

public class AgentEventRetentionOptions
{
    public bool Enabled { get; set; }
    public int RetentionDays { get; set; } = 30;
}
