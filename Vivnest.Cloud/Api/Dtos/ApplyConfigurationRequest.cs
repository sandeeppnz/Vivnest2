namespace Vivnest.Cloud.Api.Dtos;

// Decision-log.md ADR-080 - the caller-facing request body for
// POST agents/{agentId}/apply-config. Deliberately not the same shape as
// AgentConfigCommandPayload (the normalized, Cloud-computed value actually
// persisted on the command row) - this is what the dashboard/caller
// supplies, that's what CommandDispatcher resolves and validates into.
public sealed record ApplyConfigurationRequest(int ConfigurationVersion);
