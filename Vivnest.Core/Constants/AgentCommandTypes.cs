namespace Vivnest.Core.Constants;

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

    // An "ImageCapture" constant used to sit here as ExecuteCapability's
    // one accepted target alias. ADR-105 retired it: ExecuteCapability now
    // accepts exactly one capability identity, the catalogue CapabilityId,
    // and anything else is CAPABILITY_NOT_FOUND.
}
