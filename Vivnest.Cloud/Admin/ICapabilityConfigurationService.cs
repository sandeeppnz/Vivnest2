using Vivnest.Core.Domain;

namespace Vivnest.Cloud.Admin;

// Pure logic, no store - validates/defaults a DeviceCapability.Settings
// map against its Capability's ConfigurationSchema (decision-log.md
// ADR-062, Phase 5). No dependency on Azure Table Storage at all, unlike
// every other service in this namespace.
public interface ICapabilityConfigurationService
{
    // Supplied values win; any schema field missing from settings is
    // filled from capability-level DefaultConfiguration, then the
    // field's own DefaultValue. Only called once, when a DeviceCapability
    // is created/updated (spec's explicit "no inheritance beyond this one
    // hop" boundary) - never re-applied afterward.
    IReadOnlyDictionary<string, string> ApplyDefaults(
        Capability capability,
        IReadOnlyDictionary<string, string>? suppliedSettings);

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
