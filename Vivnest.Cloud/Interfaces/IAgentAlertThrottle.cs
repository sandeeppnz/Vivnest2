using Vivnest.Core.Domain;

namespace Vivnest.Cloud.Interfaces;

// Sprint 8 - see AgentAlertThrottle for why this exists and what the two
// gates are. nowUtc is a parameter rather than read inside, so cooldown and
// window-rollover behaviour is testable without waiting an hour.
public interface IAgentAlertThrottle
{
    Task<bool> ShouldNotifyAsync(
        SiteScope scope,
        string runtimeAgentId,
        string signature,
        string sampleMessage,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
