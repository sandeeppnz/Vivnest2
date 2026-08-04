namespace Vivnest.Core.Options;

public class MessagingOptions
{
    public string Transport { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string CameraCapturedQueue { get; set; } = "";
    public string AgentHeartbeatQueue { get; set; } = "";
    public string DeviceHeartbeatQueue { get; set; } = "";
    public string DeviceEventQueue { get; set; } = "";

    // Cloud-to-Agent, unlike every queue above - see
    // RestartCommandQueueMessage. The literal queue name is also hardcoded
    // in AgentsFunction's publish call and AzureAgentCommandPublisher,
    // matching how Cloud.Functions' [QueueTrigger] attributes already use
    // literal names rather than config (attribute arguments must be
    // compile-time constants) - keep all three in sync if this ever changes.
    public string RestartCommandQueue { get; set; } = "";
}