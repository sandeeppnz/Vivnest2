using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Capabilities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Admin.Interfaces;

// Pure logic, no store - validates/defaults a DeviceCapability.Settings
// map against its Capability's ConfigurationSchema (decision-log.md
// ADR-062, Phase 5). No dependency on Azure Table Storage at all, unlike
// every other service in this namespace.
public interface ICapabilityConfigurationService
{
    // Supplied values win; any schema field missing from settings is
    // filled from capability-level DefaultConfiguration, then the
    // field's own DefaultValue.
    //
    // ADR-062 originally applied this once, at DeviceCapability
    // create/update, and said "never re-applied afterward". ADR-100
    // changes that: it is now applied again at projection time. The
    // write-time pass materialises defaults into stored settings; the
    // projection-time pass covers what that cannot - a schema field added
    // after an assignment was written, and settings edited outside the
    // admin API. Re-applying is idempotent, because supplied values always
    // win.
    IReadOnlyDictionary<string, string> ApplyDefaults(
        Capability capability,
        IReadOnlyDictionary<string, string>? suppliedSettings);

    // Projection-time entry point (ADR-100). Takes the stored entity so
    // callers do not each need a fourth copy of the schema/defaults JSON
    // parsing - parsing capability configuration is this service's job.
    IReadOnlyDictionary<string, string> ResolveEffectiveSettings(
        CapabilityEntity capability,
        IReadOnlyDictionary<string, string>? storedSettings);

    // Checks required-missing, wrong type (Number/Boolean unparsable),
    // out-of-range (Number Minimum/Maximum), and not-in-AllowedValues
    // (String). Unknown keys not declared in the schema are ignored, not
    // rejected - the schema describes what a capability needs, not an
    // exhaustive allow-list.
    bool Validate(
        Capability capability,
        IReadOnlyDictionary<string, string> settings,
        out IReadOnlyList<string> errors);
}
