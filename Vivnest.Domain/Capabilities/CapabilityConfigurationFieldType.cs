

namespace Vivnest.Domain.Capabilities;

// The type of one field in a Capability's ConfigurationSchema
// (decision-log.md ADR-062, Phase 5). Deliberately a small, flat set
// instead of a real JSON Schema type system - it only needs to validate
// against the existing flat Dictionary<string,string> shape
// DeviceCapability.Settings already uses, not arbitrary nested JSON.
public enum CapabilityConfigurationFieldType
{
    String,
    Number,
    Boolean
}
