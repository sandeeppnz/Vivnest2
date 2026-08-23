namespace Vivnest.Core.Commands;

public enum CommandHandlerOutcome
{
    Succeeded,
    Failed,

    // Signals to AgentCommandPollingWorker: report Executing, then
    // IHostApplicationLifetime.StopApplication() - the same self-restart
    // CommandPollingWorker already performs for RestartAgent. Completion
    // is confirmed later by Cloud's own heartbeat-correlation hook, not
    // self-reported (the process won't be alive to report it).
    Restart
}
