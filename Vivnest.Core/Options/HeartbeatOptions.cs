namespace Vivnest.Core.Options;

public class HeartbeatOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = string.Empty;

    public string TableName { get; set; } = "Heartbeats";
}