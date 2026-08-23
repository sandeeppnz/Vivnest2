namespace Vivnest.Agent.Shell;

// The named HttpClient both command-polling workers use to call Cloud's
// agent-command API.
//
// They each built their own with `new HttpClient()` per command and
// disposed it, which is the socket-exhaustion anti-pattern - low risk at
// this volume (once per command, not per poll tick), but the project
// already resolves Home Assistant's client through IHttpClientFactory
// (AddHttpClient<IHomeAssistantCommandSender, ...>), so these two were
// the odd ones out rather than a considered exception.
//
// Registered with the Agent's own scoped key (minted at registration) as a
// default header, so the callbacks authenticate as this Agent rather than
// relying on Cloud trusting the TenantId/SiteId in the request body. Empty
// on an Agent registered before agent keys existed; Cloud still honours
// those while AgentAuth:RequireApiKey is false.
public static class CloudApiHttpClient
{
    public const string Name = "cloud-api";
}
