namespace Vivnest.Core.Options;

// Vivnest.Agent.Updater-side only - how often DeployPollingWorker checks
// agent-deploy-commands. Deploys are rare and not latency-sensitive, so
// the default is generous; configurable rather than hardcoded since a
// standalone host process is easy to reconfigure without a rebuild.
public sealed class DeployOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
}
