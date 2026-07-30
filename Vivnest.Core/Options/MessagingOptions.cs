namespace Vivnest.Core.Options;

public class MessagingOptions
{
    public string Transport { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string CameraCapturedQueue { get; set; } = "";
    public string AgentHeartbeatQueue { get; set; } = "";
    public string DeviceHeartbeatQueue { get; set; } = "";
    public string DeviceEventQueue { get; set; } = "";

}