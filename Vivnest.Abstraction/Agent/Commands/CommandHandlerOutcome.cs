namespace Vivnest.Abstraction.Agent.Commands;

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
