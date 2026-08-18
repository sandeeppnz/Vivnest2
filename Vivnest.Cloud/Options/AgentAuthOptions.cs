namespace Vivnest.Cloud.Options;

// Controls the cutover for authenticating the Agent-facing command
// callbacks (GET/PUT /agents/{agentId}/commands/{commandId}[/status]).
//
// Those two routes historically called no authenticator at all: they read
// tenantId/siteId from the query string and request body and trusted them,
// because the Agent had no credential to present. Agents now receive a
// scoped API key at registration, but agents deployed before that carry
// none - so requiring the key immediately would make their
// RefreshConfiguration/ApplyConfiguration/ExecuteCapability commands fail
// silently (TryFetchCommandAsync treats a non-success response as "no
// command" and returns without executing).
//
// Default is false: an unauthenticated callback is still honoured, but
// logged as a warning naming the agent, so the agents still needing a key
// are visible. Set to true once every agent has been re-registered or had
// its ApiKey added, at which point the endpoints reject anything without a
// matching agent key.
public sealed class AgentAuthOptions
{
    public bool RequireApiKey { get; set; }
}
