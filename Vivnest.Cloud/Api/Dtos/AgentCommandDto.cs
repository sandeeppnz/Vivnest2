namespace Vivnest.Cloud.Api.Dtos;

// Decision-log.md ADR-079 - the wire shape for GET /agents/{agentId}/commands*,
// consumed both by the dashboard (Command History) and by the Agent
// itself (AgentCommandPollingWorker's full-detail fetch before executing).
public sealed record AgentCommandDto(
    string CommandId,
    string TargetAgentId,
    string? TargetDeviceId,
    string? CapabilityId,
    string CommandType,
    string Status,
    string? Payload,
    string? Result,
    string? ErrorCode,
    string? ErrorMessage,
    string RequestedBy,
    DateTime CreatedUtc,
    DateTime? DispatchedUtc,
    DateTime? ReceivedUtc,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    DateTime ExpiresUtc,
    string TenantId,
    string SiteId);
