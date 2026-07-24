namespace Vivnest.Core.Options;

public class HeartbeatOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public string TableName { get; set; } = "Heartbeats";
    public int IntervalMinutes { get; set; } = 5;
    public int CaptureGraceMinutes { get; set; } = 5;
}