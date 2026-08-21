namespace Vivnest.Abstractions.Models.Auth;

// AgentId is null for an ordinary tenant/dashboard key and set for an
// agent key (see ApiKeyEntity.AgentId). Routes that only an Agent should
// be able to call compare it against the agent named in the route.
public sealed record TenantContext(string TenantId, string SiteId, bool DevicesOnly, string? AgentId = null);
