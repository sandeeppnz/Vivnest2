namespace Vivnest.Cloud.Api.Dtos;

// Decision-log.md ADR-081 - the caller-facing request body for
// POST agents/{agentId}/execute-capability. Deliberately Agent-centric
// (the URL names the target Agent, matching every other command route),
// not device-centric - the caller states both which Agent should execute
// and which Device/Capability, letting CommandDispatcher's existing
// ValidateAsync branch (built in Pass 1, unused until now) do the real
// authorization work exactly as designed.
public sealed record ExecuteCapabilityRequest(string TargetDeviceId, string CapabilityId);
