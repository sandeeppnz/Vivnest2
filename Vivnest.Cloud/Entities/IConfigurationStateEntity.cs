namespace Vivnest.Cloud.Entities;

// AgentConfigurationEntity and DeviceConfigurationEntity are the same row
// shape over two different tables (decision-log.md ADR-069) - both track
// only "what version is currently published, and what was its content
// hash", both guarded by ETag. This is the sliver of that shape the shared
// publish pipeline in Vivnest.Cloud.Admin.RuntimeConfigurationWriter reads
// back before deciding whether a publish is a no-op and what the next
// version number is.
//
// Read-only on purpose: the pipeline never mutates an entity it was handed,
// it constructs a fresh one through the target's CreateStateRow factory,
// because TenantId/SiteId are `required init` on BaseEntity.
//
// ETag is deliberately NOT declared here even though the pipeline needs it:
// every implementer is also an ITableEntity, which already carries it, and
// declaring it twice makes `entity.ETag` ambiguous at any call site
// constrained to both.
public interface IConfigurationStateEntity
{
    int CurrentVersion { get; }

    string CurrentHash { get; }
}
