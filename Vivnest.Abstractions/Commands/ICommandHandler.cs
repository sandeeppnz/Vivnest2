namespace Vivnest.Abstractions.Commands;

// Decision-log.md ADR-080 - a deliberate sibling to
// Runtime/Dispatching/IEventHandler<T>, not a rewrite of it: string-keyed
// by CommandType rather than CLR-generic-keyed, since a queue envelope
// carries a string, not a type. Unlike IEventHandler<T> (many handlers can
// exist per event type, EventDispatcher runs all of them), exactly one
// ICommandHandler is expected per CommandType - PlatformAgentCommandPollingWorker
// resolves a single match, not a collection.
public interface ICommandHandler
{
    string CommandType { get; }

    Task<CommandHandlerResult> HandleAsync(AgentCommandDetails command, CancellationToken cancellationToken);
}

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

public enum CommandHandlerOutcome
{
    Succeeded,
    Failed,

    // Signals to PlatformAgentCommandPollingWorker: report Executing, then
    // IHostApplicationLifetime.StopApplication() - the same self-restart
    // PlatformCommandPollingWorker already performs for RestartAgent. Completion
    // is confirmed later by Cloud's own heartbeat-correlation hook, not
    // self-reported (the process won't be alive to report it).
    Restart
}

public sealed record CommandHandlerResult(
    CommandHandlerOutcome Outcome,
    string? Result = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static CommandHandlerResult Succeeded(string? result = null) =>
        new(CommandHandlerOutcome.Succeeded, Result: result);

    public static CommandHandlerResult Failed(string errorCode, string errorMessage) =>
        new(CommandHandlerOutcome.Failed, ErrorCode: errorCode, ErrorMessage: errorMessage);

    public static CommandHandlerResult Restart() => new(CommandHandlerOutcome.Restart);
}
