namespace Vivnest.Abstraction.Agent.Commands;

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
