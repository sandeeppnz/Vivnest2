namespace Vivnest.Core.Options;

// Queue names default to their canonical values (ADR-120) - like table
// names they are platform constants (several are also hardcoded as
// compile-time literals in [QueueTrigger] attributes and publisher
// constants, per the notes below), so absence in config means the
// canonical name, not a broken empty string. ConnectionString stays
// empty: it is the one genuinely per-deployment secret here.
public class MessagingOptions
{
    public string ConnectionString { get; set; } = "";
    public string CameraCapturedQueue { get; set; } = "camera-captured";
    public string AgentHeartbeatQueue { get; set; } = "agent-heartbeats";
    public string DeviceHeartbeatQueue { get; set; } = "device-heartbeats";
    public string DeviceEventQueue { get; set; } = "device-events";

    // Cloud-to-Agent, unlike every queue above - see
    // RestartCommandQueueMessage. The literal queue name is also hardcoded
    // in AgentsFunction's publish call and AzureAgentCommandPublisher,
    // matching how Cloud.Functions' [QueueTrigger] attributes already use
    // literal names rather than config (attribute arguments must be
    // compile-time constants) - keep all three in sync if this ever changes.
    public string RestartCommandQueue { get; set; } = "agent-restart-commands";

    // Cloud-to-Agent, same reasoning as RestartCommandQueue - but this one
    // is consumed by Vivnest.Agent.Updater (a separate host-level process),
    // never by Vivnest.Agent itself, since the Agent container deliberately
    // has no Docker access (ADR-020/ADR-028).
    public string DeployCommandQueue { get; set; } = "agent-deploy-commands";

    // Agent-to-Cloud, Low-type only (ADR-035, design 3) - a Low-type
    // agent's SinkCleanlinessHandler publishes here instead of an
    // in-process Channel<T>. The literal queue name is also hardcoded in
    // ClassifyRequestFunction's [QueueTrigger] attribute - keep both in
    // sync if this ever changes.
    public string ClassifyRequestQueue { get; set; } = "classify-requests";

    // Cloud-to-Agent, High-type only, same reasoning as RestartCommandQueue -
    // the literal queue name is also hardcoded in AgentCommandPublisher's
    // ClassifyCommandQueueName constant - keep both in sync.
    public string ClassifyCommandQueue { get; set; } = "agent-classify-commands";

    // Agent-to-Cloud, Sprint 8 - mirrors DeviceEventQueue exactly, including
    // the {PartitionKey, RowKey}-only message shape (ADR-004). Separate
    // from DeviceEventQueue rather than shared, because the consumer
    // refetches from a different table.
    public string AgentEventQueue { get; set; } = "agent-events";

    // Decision-log.md ADR-079 - Cloud-to-Agent, shared by RefreshConfiguration/
    // ApplyConfiguration/ExecuteCapability (all consumed by one new
    // AgentCommandPollingWorker, so one shared queue is consistent with
    // ADR-024's own "one queue per consumer" rule) - literal queue name
    // also hardcoded in AgentCommandPublisher's AgentCommandQueueName
    // constant, keep both in sync.
    public string AgentCommandQueue { get; set; } = "agent-commands";
}