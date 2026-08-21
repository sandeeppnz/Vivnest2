namespace Vivnest.Abstractions.Domain;

// The one place the Tenant+Site Azure Table partition-key convention is
// defined. Every existing site-scoped entity/service already builds
// $"{TenantId}|{SiteId}" independently (AgentHeartbeatWriter,
// DeviceHeartbeatWriter, HealthMonitorService, DeviceQueryService,
// AgentRegistryManagementService, DeviceRegistryManagementService) - this
// doesn't change what any of them produce, it gives them one shared
// definition to call instead of six independent copies of the same
// string interpolation.
public readonly record struct SiteScope(string TenantId, string SiteId)
{
    public string PartitionKey => $"{TenantId}|{SiteId}";

    public static SiteScope From(ISiteScoped entity) => new(entity.TenantId, entity.SiteId);
}
