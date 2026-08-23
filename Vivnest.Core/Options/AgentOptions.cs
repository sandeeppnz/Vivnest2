using Vivnest.Domain.Agents;

namespace Vivnest.Core.Options;


public class AgentOptions
{
    public string AgentId { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;

    // Decision-log.md ADR-079 - the first time Vivnest.Agent itself (not
    // just the separate Updater process) needs to call back to the Cloud
    // Functions HTTP API, for command status-transition callbacks
    // (CommandPollingWorker's Received report, AgentCommandPollingWorker's
    // full-detail fetch and Received/Executing/Succeeded/Failed reports).
    // Every prior Agent-to-Cloud interaction went through Storage
    // primitives (Queue/Table/Blob) directly via Storage:ConnectionString -
    // this is genuinely new config, not a rename of something that
    // already existed.
    public string CloudApiBaseUrl { get; set; } = string.Empty;

    // Written into appsettings.json by the Updater at registration. Sent
    // as x-api-key on the Agent-facing command callbacks. Empty on any
    // Agent registered before agent keys existed - Cloud tolerates that
    // while AgentAuth:RequireApiKey is false (see AgentAuthOptions).
    public string ApiKey { get; set; } = string.Empty;

    // Which capabilities this process registers - see Program.cs and
    // decision-log.md ADR-035/ADR-044. Defaults to Low so every existing
    // agent config needs zero changes.
    public AgentType Type { get; set; } = AgentType.Low;
}
