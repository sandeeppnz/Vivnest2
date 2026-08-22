namespace Vivnest.Abstraction.Agent.Commands;

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


