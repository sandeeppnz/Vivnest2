namespace Vivnest.Core.Runtime;

// Sprint 8. One Error-level log call, captured for the worker that turns it
// into an AgentEvent.
public sealed record AgentErrorSignal(string Category, string Message, string? Exception);
