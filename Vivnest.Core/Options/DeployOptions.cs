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

    // ACR repository-scoped token credentials (ADR-039) - if both are set,
    // AgentDeployer logs in to the registry itself before every pull,
    // rather than depending on a prior manual `az acr login` on this host
    // (which is tied to an Azure CLI session and expires if nobody's been
    // at the machine recently - exactly the failure found live: a
    // queue-triggered deploy failing with no human present to re-auth).
    // Both default empty - additive, not required, same convention as
    // everything else in this config system: an instance that hasn't set
    // these yet just skips straight to pull, unchanged from today.
    public string AcrUsername { get; set; } = "";
    public string AcrPassword { get; set; } = "";
}
