namespace Vivnest.Abstractions.Constants;

// Decision-log.md ADR-079 - Phase 9's four command types. String-valued
// (not an enum) because the value travels as-is through
// AgentCommandQueueMessage/AgentCommandEntity.CommandType and the Agent's
// own string-keyed ICommandHandler registry - same reasoning
// AgentEventTypes/DeviceEventTypes/NotificationTypes already use for
// their own wire-format constants.
public static class AgentCommandTypes
{
    public const string RestartAgent = "RestartAgent";
    public const string RefreshConfiguration = "RefreshConfiguration";
    public const string ApplyConfiguration = "ApplyConfiguration";
    public const string ExecuteCapability = "ExecuteCapability";

    // Decision-log.md ADR-079 - the one ExecuteCapability target this
    // phase actually executes. Deliberately not a real Capability admin
    // Guid: Image Capture is Built-in (no DeviceCapability/ExecutingAgentId
    // row exists for it - see DeviceCapabilitiesQueryService's own
    // BuildCapabilitiesAsync), so its authorization check is
    // Device.OwningAgentId, not the DeviceCapability/ExecutingAgentId
    // chain every other CapabilityId value goes through.
    public const string ImageCaptureCapabilityId = "ImageCapture";
}
