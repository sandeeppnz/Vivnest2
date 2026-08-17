namespace Vivnest.Core.Options;

public class MessagingOptions
{
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

    // Cloud-to-Agent, same reasoning as RestartCommandQueue - but this one
    // is consumed by Vivnest.Agent.Updater (a separate host-level process),
    // never by Vivnest.Agent itself, since the Agent container deliberately
    // has no Docker access (ADR-020/ADR-028).
    public string DeployCommandQueue { get; set; } = "";

    // Agent-to-Cloud, Low-type only (ADR-035, design 3) - a Low-type
    // agent's SinkCleanlinessHandler publishes here instead of an
    // in-process Channel<T>. The literal queue name is also hardcoded in
    // ClassifyRequestFunction's [QueueTrigger] attribute - keep both in
    // sync if this ever changes.
    public string ClassifyRequestQueue { get; set; } = "";

    // Cloud-to-Agent, High-type only, same reasoning as RestartCommandQueue -
    // the literal queue name is also hardcoded in AgentCommandPublisher's
    // ClassifyCommandQueueName constant - keep both in sync.
    public string ClassifyCommandQueue { get; set; } = "";

    // Decision-log.md ADR-079 - Cloud-to-Agent, shared by RefreshConfiguration/
    // ApplyConfiguration/ExecuteCapability (all consumed by one new
    // PlatformAgentCommandPollingWorker, so one shared queue is consistent with
    // ADR-024's own "one queue per consumer" rule) - literal queue name
    // also hardcoded in AgentCommandPublisher's AgentCommandQueueName
    // constant, keep both in sync.
    public string AgentCommandQueue { get; set; } = "";
}