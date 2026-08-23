

namespace Vivnest.Domain.Capabilities;

// decision-log.md ADR-062 (Phase 5) - only Required is implemented.
// Optional/Alternative are named in the spec as possible future values
// but deliberately not given any behaviour yet (nothing in the product
// needs them) - kept as a single-member enum instead of a bool so a
// future dependency type doesn't require a breaking field-type change.
public enum CapabilityDependencyType
{
    Required
}
