// NOT the same type as Vivnest.Domain.Capabilities.CapabilityDependency,
// despite the name. This is the MANIFEST declaration - what a running
// capability says it needs, by CapabilityKey and minimum version, read
// from ICapability.Manifest at runtime. The Domain one is the CATALOGUE
// row in tblCapabilityDependencies: an edge in the global dependency
// graph with its own DependencyId and DependencyType, authored in Admin
// and used by Cloud to validate assignments.
//
// Same collision as the two CapabilityStatus types, and for the same
// reason: the runtime and the catalogue describe overlapping concepts
// from opposite sides. Different assemblies keep them apart; a file
// needing both must alias one.

namespace Vivnest.Core.Capabilities;

public sealed record CapabilityDependency(
    string CapabilityId,
    string MinimumVersion);