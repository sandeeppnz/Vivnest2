namespace Vivnest.Abstraction.Agent.Commands;

// The Agent's own local copy of the command detail shape fetched via
// GET /agents/{agentId}/commands/{commandId} - mirrors Vivnest.Cloud's
// AgentCommandDto field-for-field, but declared independently since
// Vivnest.Agent doesn't (and shouldn't) reference Vivnest.Cloud. Only the
// fields handlers actually need are kept - Status/ExpiresUtc (decision-log.md
// ADR-082) exist purely for PlatformAgentCommandPollingWorker's own pre-execution
// check, not for any ICommandHandler implementation to read.
public sealed record AgentCommandDetails(
    string CommandId,
    string TargetAgentId,
    string? TargetDeviceId,
    string? CapabilityId,
    string CommandType,
    string? Payload,
    string Status,
    DateTime ExpiresUtc);
