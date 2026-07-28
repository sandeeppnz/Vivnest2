namespace Vivnest.Core.Options;

public class MessagingOptions
{
    public string Transport { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string CameraCapturedQueue { get; set; } = "";
    public string HeartbeatQueue { get; set; } = "";

}