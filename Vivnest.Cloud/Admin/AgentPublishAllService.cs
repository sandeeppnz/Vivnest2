using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Domain.Devices;

namespace Vivnest.Cloud.Admin;

// "Publish all & refresh" (ADR-121) - composes the two existing
// publishers and the command dispatcher rather than adding any new write
// path: every step here is exactly what the per-device / per-agent
// buttons already do, just sequenced so it cannot be half-done. Failures
// and gates are per-item report lines, never exceptions - one blocked
// device must not stop the agent publish or the refresh, because
// bringing the agent in line with whatever IS publishable is the whole
// point.
public sealed class AgentPublishAllService : IAgentPublishAllService
{
    private readonly IAgentRegistryStore _agentRegistry;
    private readonly IDeviceService _devices;
    private readonly IDeviceRuntimeConfigurationPublisher _devicePublisher;
    private readonly IAgentRuntimeConfigurationPublisher _agentPublisher;
    private readonly ICommandDispatcher _commandDispatcher;

    public AgentPublishAllService(
        IAgentRegistryStore agentRegistry,
        IDeviceService devices,
        IDeviceRuntimeConfigurationPublisher devicePublisher,
        IAgentRuntimeConfigurationPublisher agentPublisher,
        ICommandDispatcher commandDispatcher)
    {
        _agentRegistry = agentRegistry;
        _devices = devices;
        _devicePublisher = devicePublisher;
        _agentPublisher = agentPublisher;
        _commandDispatcher = commandDispatcher;
    }

    public async Task<PublishAllReport?> PublishAllAsync(
        TenantContext tenant,
        string runtimeAgentId,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        // The publishers live in the admin AgentId space; the route (and
        // the refresh command) live in the RuntimeAgentId space - the
        // same identity-space crossing CommandDispatcher documents at
        // its ADR-081 check.
        var registry = await _agentRegistry.GetByRuntimeAgentIdAsync(
            tenant.TenantId, tenant.SiteId, runtimeAgentId, cancellationToken);

        if (registry == null)
            return null;

        var items = new List<PublishAllItem>();

        // Devices first, agent second: the agent blob's Capabilities
        // list is what the agent acts on at refresh time, so it must be
        // the freshest thing written before the refresh goes out.
        var devices = await _devices.ListAsync(
            tenant, ownerAgentId: registry.RowKey, cancellationToken: cancellationToken);

        foreach (var device in devices)
        {
            if (!string.Equals(device.Status, DeviceStatus.Active.ToString(), StringComparison.Ordinal))
            {
                items.Add(new PublishAllItem(
                    "device", device.Name, "skipped", $"status is {device.Status}"));
                continue;
            }

            try
            {
                var result = await _devicePublisher.PublishAsync(
                    tenant, device.DeviceId.ToString(), cancellationToken);

                items.Add(ToItem("device", device.Name, result?.Published, result?.Reason));
            }
            catch (Exception ex)
            {
                items.Add(new PublishAllItem("device", device.Name, "failed", ex.Message));
            }
        }

        try
        {
            var agentResult = await _agentPublisher.PublishAsync(
                tenant, registry.RowKey, cancellationToken);

            items.Add(ToItem("agent", registry.Name, agentResult?.Published, agentResult?.Reason));
        }
        catch (Exception ex)
        {
            items.Add(new PublishAllItem("agent", registry.Name, "failed", ex.Message));
        }

        // Always refresh, even when everything above was unchanged - the
        // agent may have missed an earlier refresh, and "bring it in
        // line" is what the caller asked for. One extra no-op refresh in
        // command history is the cheap side of that trade.
        var refreshQueued = false;

        try
        {
            var command = await _commandDispatcher.DispatchAsync(
                tenant,
                AgentCommandTypes.RefreshConfiguration,
                runtimeAgentId,
                requestedBy,
                cancellationToken: cancellationToken);

            refreshQueued = command != null;

            items.Add(command != null
                ? new PublishAllItem("refresh", registry.Name, "queued", null)
                : new PublishAllItem(
                    "refresh", registry.Name, "failed",
                    "agent not found in the runtime agent list - has it ever sent a heartbeat?"));
        }
        catch (Exception ex)
        {
            items.Add(new PublishAllItem("refresh", registry.Name, "failed", ex.Message));
        }

        return new PublishAllReport(items, refreshQueued);
    }

    private static PublishAllItem ToItem(string kind, string name, bool? published, string? reason)
    {
        if (published == null)
            return new PublishAllItem(kind, name, "failed", "not found");

        if (published.Value)
            return new PublishAllItem(kind, name, "published", null);

        // The ADR-069 no-op guard's own wording ("Configuration unchanged
        // since version N.") is the only way the publishers distinguish
        // "nothing to do" from a real gate - RuntimeConfigurationWriter
        // produces it, both publishers pass it through verbatim.
        var unchanged = reason?.StartsWith("Configuration unchanged", StringComparison.Ordinal) == true;

        return new PublishAllItem(kind, name, unchanged ? "unchanged" : "blocked", reason);
    }
}
