namespace Vivnest.Core.Queues.Models;

// Mirrors RestartCommandQueueMessage exactly - same Cloud-to-Agent shape,
// same reasoning (ADR-004 doesn't apply, there's no persisted row to
// reference).
//
// ImageVersion (decision-log.md ADR-073) - the target's active
// AgentInstallation.ImageVersion at publish time, resolved by whichever
// producer is enqueueing this (AgentInstallationManagementService.RegisterAsync,
// or AgentsFunction.DeployAgent). Null means "no desired version set" and
// AgentDeployer falls back to :latest, unchanged from before this field
// existed - a message enqueued by an older Cloud build (or a caller that
// genuinely has no ImageVersion to resolve) still deploys exactly as it
// always did.
public sealed record DeployCommandQueueMessage(
    string AgentId,
    DateTime IssuedAtUtc,
    string? ImageVersion = null);
