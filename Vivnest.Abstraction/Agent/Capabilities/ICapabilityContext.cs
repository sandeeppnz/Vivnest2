namespace Vivnest.Abstraction.Agent.Capabilities;

// One context per capability startup, not one per Agent (ADR-101). The
// runtime hands a capability its own assignment rather than making the
// capability go looking for itself in a store - which also keeps every
// capability free of any dependency on runtime infrastructure.
public interface ICapabilityContext
{
    string AgentId { get; }

    string? TenantId { get; }

    string? SiteId { get; }

    IServiceProvider Services { get; }

    // The assignment that selected this capability for startup. Its
    // CapabilityId always equals the capability's own Manifest.Id -
    // CapabilityHost enforces that before calling StartAsync.
    RuntimeCapabilityAssignment Assignment { get; }
}