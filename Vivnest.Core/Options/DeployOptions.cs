namespace Vivnest.Core.Options;

// Vivnest.Agent.Updater-side only - how often DeployPollingWorker checks
// agent-deploy-commands. Deploys are rare and not latency-sensitive, so
// the default is generous; configurable rather than hardcoded since a
// standalone host process is easy to reconfigure without a rebuild.
public sealed class DeployOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    // Which container this Updater instance manages - was a hardcoded
    // "vivnest-agent" constant until two agents (Capture + Ai role) ever
    // needed to run on the same Docker host, the exact trigger
    // DeployPollingWorker's own original comment named. Default preserves
    // today's single-agent deployments with no config change needed;
    // a second Updater instance (its own folder, its own
    // updater.settings.json, its own Agent:AgentId to filter on) sets this
    // to something distinct, e.g. "vivnest-agent-ai".
    public string ContainerName { get; set; } = "vivnest-agent";
}
