using Vivnest.Domain.Agents;
using Vivnest.Domain.Shared;

namespace Vivnest.Domain.Agents;

// Decision-log.md ADR-079 - the persisted record of a single Admin ->
// Agent command, mirroring AgentInstallation's own shape exactly (Phase 7
// Pass 1, ADR-071): a thin, mostly-permissive domain object with named
// transition methods, existence/ownership validated by the caller
// (CommandDispatcher) before this object is even constructed, not inside
// it. CommandId is a generated Guid, doubling as the Agent's idempotency
// key - see AgentCommandManagementService's status-transition guard for
// where that's actually enforced.
public sealed class AgentCommand : ISiteScoped
{
    public string TenantId { get; private set; } = null!;

    public string SiteId { get; private set; } = null!;

    public string CommandId { get; private set; } = null!;

    public string TargetAgentId { get; private set; } = null!;

    public string? TargetDeviceId { get; private set; }

    public string? CapabilityId { get; private set; }

    public string CommandType { get; private set; } = null!;

    public AgentCommandStatus Status { get; private set; }

    // JSON-serialized, command-type-specific - e.g.
    // {"configurationVersion":21} for ApplyConfiguration. Opaque to this
    // class, interpreted only by the matching command handler.
    public string? Payload { get; private set; }

    public string? Result { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string RequestedBy { get; private set; } = null!;

    public DateTime CreatedUtc { get; private set; }

    public DateTime? DispatchedUtc { get; private set; }

    public DateTime? ReceivedUtc { get; private set; }

    public DateTime? StartedUtc { get; private set; }

    public DateTime? CompletedUtc { get; private set; }

    public DateTime ExpiresUtc { get; private set; }

    private AgentCommand()
    {
    }

    public AgentCommand(
        string tenantId,
        string siteId,
        string targetAgentId,
        string commandType,
        TimeSpan expiresAfter,
        string requestedBy,
        string? targetDeviceId = null,
        string? capabilityId = null,
        string? payload = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("SiteId is required.", nameof(siteId));

        if (string.IsNullOrWhiteSpace(targetAgentId))
            throw new ArgumentException("TargetAgentId is required.", nameof(targetAgentId));

        if (string.IsNullOrWhiteSpace(commandType))
            throw new ArgumentException("CommandType is required.", nameof(commandType));

        if (string.IsNullOrWhiteSpace(requestedBy))
            throw new ArgumentException("RequestedBy is required.", nameof(requestedBy));

        TenantId = tenantId;
        SiteId = siteId;
        CommandId = Guid.NewGuid().ToString();
        TargetAgentId = targetAgentId;
        TargetDeviceId = targetDeviceId;
        CapabilityId = capabilityId;
        CommandType = commandType;
        Payload = payload;
        RequestedBy = requestedBy;

        Status = AgentCommandStatus.Pending;
        CreatedUtc = DateTime.UtcNow;
        ExpiresUtc = CreatedUtc.Add(expiresAfter);
    }

    public static AgentCommand Rehydrate(
        string tenantId,
        string siteId,
        string commandId,
        string targetAgentId,
        string? targetDeviceId,
        string? capabilityId,
        string commandType,
        AgentCommandStatus status,
        string? payload,
        string? result,
        string? errorCode,
        string? errorMessage,
        string requestedBy,
        DateTime createdUtc,
        DateTime? dispatchedUtc,
        DateTime? receivedUtc,
        DateTime? startedUtc,
        DateTime? completedUtc,
        DateTime expiresUtc)
    {
        return new AgentCommand
        {
            TenantId = tenantId,
            SiteId = siteId,
            CommandId = commandId,
            TargetAgentId = targetAgentId,
            TargetDeviceId = targetDeviceId,
            CapabilityId = capabilityId,
            CommandType = commandType,
            Status = status,
            Payload = payload,
            Result = result,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            RequestedBy = requestedBy,
            CreatedUtc = createdUtc,
            DispatchedUtc = dispatchedUtc,
            ReceivedUtc = receivedUtc,
            StartedUtc = startedUtc,
            CompletedUtc = completedUtc,
            ExpiresUtc = expiresUtc
        };
    }

    // Pending -> Dispatched: persisted and successfully enqueued.
    public void MarkDispatched()
    {
        Status = AgentCommandStatus.Dispatched;
        DispatchedUtc = DateTime.UtcNow;
    }

    // Dispatched -> Received: the Agent's command dispatcher picked up
    // the message and passed its own identity/expiry checks. Best-effort
    // - some commands (RestartAgent) may never reach this before the
    // process exits.
    public void MarkReceived()
    {
        Status = AgentCommandStatus.Received;
        ReceivedUtc = DateTime.UtcNow;
    }

    // Received -> Executing: the Agent has started real work.
    public void MarkExecuting()
    {
        Status = AgentCommandStatus.Executing;
        StartedUtc ??= DateTime.UtcNow;
    }

    public void MarkSucceeded(string? result)
    {
        Status = AgentCommandStatus.Succeeded;
        Result = result;
        CompletedUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string errorCode, string errorMessage)
    {
        Status = AgentCommandStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CompletedUtc = DateTime.UtcNow;
    }

    public void MarkExpired()
    {
        Status = AgentCommandStatus.Expired;
        CompletedUtc = DateTime.UtcNow;
    }
}
