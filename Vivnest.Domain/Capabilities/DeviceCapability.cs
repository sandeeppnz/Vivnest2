using Vivnest.Domain.Agents;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Shared;
using Vivnest.Domain.Tenants;

namespace Vivnest.Domain.Capabilities;

// A capability assigned to a specific Device (decision-log.md ADR-057) -
// the join Device --- DeviceCapability --- Capability, carrying the one
// fact neither Device nor Capability can express alone: which Agent
// actually executes this capability for this device. Modeled after
// AgentInstallation, not after the flat master lists (Device/DeviceType/
// Capability) - like an installation, an assignment is a lifecycle
// (Assign/Unassign), not reference data, so it soft-removes (Status)
// rather than hard-deleting.
//
// ExecutingAgentId is deliberately distinct from Device.OwningAgentId -
// OwningAgentId is "which agent physically owns/manages this device,"
// ExecutingAgentId is "which agent executes this one capability" - a
// different agent can execute a capability than the one that owns the
// device (e.g. a Low-type agent owns a camera, a separate High-type
// agent executes its Object Detection capability). Same split
// DeviceOptions/SinkCleanlinessRoiOptions/ObjectDetectionRoiOptions
// already established in the MVP runtime config - this is that same
// relationship, now expressible as a real persisted record instead of a
// hardcoded field per capability.
public sealed class DeviceCapability : ISiteScoped
{
    public string TenantId { get; private set; } = null!;

    public string SiteId { get; private set; } = null!;

    public string DeviceCapabilityId { get; private set; } = null!;

    public string DeviceId { get; private set; } = null!;

    public string CapabilityId { get; private set; } = null!;

    public string ExecutingAgentId { get; private set; } = "";

    public bool Enabled { get; private set; }

    public IReadOnlyDictionary<string, string> Settings { get; private set; } =
        new Dictionary<string, string>();

    public DeviceCapabilityStatus Status { get; private set; }

    public DateTime AssignedUtc { get; private set; }

    public DateTime? RemovedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    private DeviceCapability()
    {
    }

    public DeviceCapability(
        string tenantId,
        string siteId,
        string deviceId,
        string capabilityId,
        string executingAgentId = "",
        bool enabled = true,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("SiteId is required.", nameof(siteId));

        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));

        if (string.IsNullOrWhiteSpace(capabilityId))
            throw new ArgumentException("CapabilityId is required.", nameof(capabilityId));

        TenantId = tenantId;
        SiteId = siteId;
        DeviceCapabilityId = Guid.NewGuid().ToString();
        DeviceId = deviceId;
        CapabilityId = capabilityId;
        ExecutingAgentId = executingAgentId;
        Enabled = enabled;
        Settings = settings ?? new Dictionary<string, string>();

        Status = DeviceCapabilityStatus.Active;

        AssignedUtc = DateTime.UtcNow;
        UpdatedUtc = AssignedUtc;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static DeviceCapability Rehydrate(
        string tenantId,
        string siteId,
        string deviceCapabilityId,
        string deviceId,
        string capabilityId,
        string executingAgentId,
        bool enabled,
        IReadOnlyDictionary<string, string> settings,
        DeviceCapabilityStatus status,
        DateTime assignedUtc,
        DateTime? removedUtc,
        DateTime updatedUtc)
    {
        return new DeviceCapability
        {
            TenantId = tenantId,
            SiteId = siteId,
            DeviceCapabilityId = deviceCapabilityId,
            DeviceId = deviceId,
            CapabilityId = capabilityId,
            ExecutingAgentId = executingAgentId,
            Enabled = enabled,
            Settings = settings,
            Status = status,
            AssignedUtc = assignedUtc,
            RemovedUtc = removedUtc,
            UpdatedUtc = updatedUtc
        };
    }

    public void Update(
        string executingAgentId,
        bool enabled,
        IReadOnlyDictionary<string, string>? settings)
    {
        ExecutingAgentId = executingAgentId;
        Enabled = enabled;
        Settings = settings ?? new Dictionary<string, string>();
        UpdatedUtc = DateTime.UtcNow;
    }

    // Marks this assignment Removed - same "retire, never mutate history"
    // reasoning as AgentInstallation.Remove().
    public void Remove()
    {
        Status = DeviceCapabilityStatus.Removed;
        RemovedUtc = DateTime.UtcNow;
        UpdatedUtc = RemovedUtc.Value;
    }
}
