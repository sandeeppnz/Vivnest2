using Vivnest.Core.Enums;

namespace Vivnest.Core.Domain;

// A specific deployment of an Agent onto a Machine - the "which
// deployment" identity, distinct from Agent ("who") and Machine
// ("where"). InstallationId is a generated Guid (like Capability/
// AgentRegistry/DeviceRegistry ids) - unlike Machine, an installation
// isn't something an operator names, it's the record of a lifecycle
// action (Install/Move/Uninstall). AgentId and MachineId are relationship
// properties, not validated for existence here - existence is checked by
// AgentInstallationManagementService before a domain object is even
// constructed (decision-log.md ADR-053), same reasoning ADR-052 already
// established for API keys: validate at the point something real gets
// created, not inside the domain model itself.
public sealed class AgentInstallation : ISiteScoped
{
    public string TenantId { get; private set; } = null!;

    public string SiteId { get; private set; } = null!;

    public string InstallationId { get; private set; } = null!;

    public string AgentId { get; private set; } = null!;

    public string MachineId { get; private set; } = null!;

    public string? ContainerId { get; private set; }

    public string? ImageName { get; private set; }

    public string? ImageVersion { get; private set; }

    public AgentInstallationStatus Status { get; private set; }

    public DateTime InstalledUtc { get; private set; }

    public DateTime? RemovedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    private AgentInstallation()
    {
    }

    public AgentInstallation(
        string tenantId,
        string siteId,
        string agentId,
        string machineId,
        string? containerId = null,
        string? imageName = null,
        string? imageVersion = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("SiteId is required.", nameof(siteId));

        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("AgentId is required.", nameof(agentId));

        if (string.IsNullOrWhiteSpace(machineId))
            throw new ArgumentException("MachineId is required.", nameof(machineId));

        TenantId = tenantId;
        SiteId = siteId;
        InstallationId = Guid.NewGuid().ToString();
        AgentId = agentId;
        MachineId = machineId;
        ContainerId = containerId;
        ImageName = imageName;
        ImageVersion = imageVersion;

        // Decision-log.md ADR-071 - every installation starts Pending, an
        // install token issued alongside it (AgentInstallationManagementService) -
        // nothing is assumed running until the registration endpoint (or a
        // real heartbeat) proves otherwise.
        Status = AgentInstallationStatus.Pending;

        InstalledUtc = DateTime.UtcNow;
        UpdatedUtc = InstalledUtc;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static AgentInstallation Rehydrate(
        string tenantId,
        string siteId,
        string installationId,
        string agentId,
        string machineId,
        string? containerId,
        string? imageName,
        string? imageVersion,
        AgentInstallationStatus status,
        DateTime installedUtc,
        DateTime? removedUtc,
        DateTime updatedUtc)
    {
        return new AgentInstallation
        {
            TenantId = tenantId,
            SiteId = siteId,
            InstallationId = installationId,
            AgentId = agentId,
            MachineId = machineId,
            ContainerId = containerId,
            ImageName = imageName,
            ImageVersion = imageVersion,
            Status = status,
            InstalledUtc = installedUtc,
            RemovedUtc = removedUtc,
            UpdatedUtc = updatedUtc
        };
    }

    // Decision-log.md ADR-071 - named transitions replacing the single
    // Remove() this class used to have, one per real lifecycle event.
    // Deliberately not state-machine-validated (no "throw if not Pending")
    // - same permissive style the rest of this codebase's status setters
    // use (e.g. Machine.SetStatus) - callers are trusted to invoke the
    // right transition at the right time, consistent with how thin this
    // domain layer already is elsewhere.

    // Pending -> Installing: the registration endpoint just assigned a
    // RuntimeAgentId (or the target already had one, for a Move) and
    // enqueued the first deploy command.
    public void Register()
    {
        Status = AgentInstallationStatus.Installing;
        UpdatedUtc = DateTime.UtcNow;
    }

    // Installing/Updating -> Installed: the Updater reported a successful
    // docker deploy via the deploy-complete callback.
    public void MarkInstalled()
    {
        Status = AgentInstallationStatus.Installed;
        UpdatedUtc = DateTime.UtcNow;
    }

    // Installed -> Active: a real heartbeat was received - the strongest
    // signal available that the container is genuinely running.
    public void MarkActive()
    {
        Status = AgentInstallationStatus.Active;
        UpdatedUtc = DateTime.UtcNow;
    }

    // Active -> Updating: a new deploy was just issued against an
    // already-running installation (e.g. a version bump).
    public void MarkUpdating()
    {
        Status = AgentInstallationStatus.Updating;
        UpdatedUtc = DateTime.UtcNow;
    }

    // Any -> Decommissioned (terminal) - used both when uninstalling an
    // Agent outright and when moving it to a new Machine (the old
    // installation is retired, never mutated to point at the new
    // Machine - see decision-log.md ADR-053, "preserve installation
    // history"). RemovedUtc keeps its original field name (no entity/table
    // schema churn for a rename) but now means "DecommissionedUtc."
    public void Decommission()
    {
        Status = AgentInstallationStatus.Decommissioned;
        RemovedUtc = DateTime.UtcNow;
        UpdatedUtc = RemovedUtc.Value;
    }
}
