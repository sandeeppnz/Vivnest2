using Vivnest.Cloud.Admin;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Tests;

// ADR-102 (Command Routing 1.5). Three spellings of one capability reached
// the command path:
//
//   "ImageCapture"   legacy Cloud command alias (AgentCommandTypes)
//   5217f0ef-...     catalogue RowKey
//   camera.capture   runtime CapabilityKey (CapabilityManifest.Id)
//
// The runtime understands only the third. Rather than teach it a second
// spelling, Cloud normalizes the first two to the catalogue RowKey when the
// command is stored, and GetCommand translates RowKey -> CapabilityKey on
// the way out (CommandRuntimeIdentityTests). Both inputs converge.
//
// "ImageCapture" resolves through RuntimeNameMatch against the catalogue's
// CapabilityName "Image Capture" - the same strip-spaces, case-insensitive
// rule four other call sites already share. Deliberately not a hard-coded
// "ImageCapture" -> "camera.capture" table, which would add a fourth
// identity space instead of bridging out of the third.
public class LegacyCapabilityIdentityTests
{
    private const string CatalogueGuid = "5217f0ef-f7c6-4d9f-9723-7bf2afad5572";

    private static readonly IReadOnlyList<CapabilityEntity> Catalogue =
    [
        new()
        {
            PartitionKey = "capability",
            RowKey = CatalogueGuid,
            CapabilityName = "Image Capture",
            CapabilityKey = "camera.capture",
            CapabilityType = "Device",
            Status = "Active"
        },
        new()
        {
            PartitionKey = "capability",
            RowKey = "c3519eb0-f2f5-4fc2-b9ea-9249becbb964",
            CapabilityName = "Sink Cleanliness",
            CapabilityKey = "sink.cleanliness",
            CapabilityType = "Service",
            Status = "Active"
        }
    ];

    // The legacy alias, resolved through the catalogue rather than a table.
    [Fact]
    public void LegacyImageCaptureIsNormalizedToTheCatalogueId()
    {
        Assert.Equal(CatalogueGuid, CommandDispatcher.Normalize("ImageCapture", Catalogue));
    }

    // The alias differs from CapabilityName only by a space, which is
    // exactly what RuntimeNameMatch normalizes - and case too.
    [Theory]
    [InlineData("ImageCapture")]
    [InlineData("imagecapture")]
    [InlineData("Image Capture")]
    [InlineData("IMAGE CAPTURE")]
    public void SpacingAndCaseVariantsAllResolve(string supplied)
    {
        Assert.Equal(CatalogueGuid, CommandDispatcher.Normalize(supplied, Catalogue));
    }

    // A caller already using the catalogue id is left alone - and matched
    // by RowKey before any name matching is attempted, so a capability
    // whose NAME happened to look like a GUID could not hijack it.
    [Fact]
    public void ACatalogueIdIsUnchanged()
    {
        Assert.Equal(CatalogueGuid, CommandDispatcher.Normalize(CatalogueGuid, Catalogue));
    }

    // Normalization must never turn a command Cloud accepted into one it
    // cannot record, so anything unresolvable passes through untouched and
    // fails later where the reason is reported.
    [Theory]
    [InlineData("no.such.capability")]
    [InlineData("Totally Unknown")]
    public void AnUnresolvableValueIsUnchanged(string supplied)
    {
        Assert.Equal(supplied, CommandDispatcher.Normalize(supplied, Catalogue));
    }

    // RefreshConfiguration, ApplyConfiguration and RestartAgent carry none.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAbsentCapabilityIsUnchanged(string? supplied)
    {
        Assert.Equal(supplied, CommandDispatcher.Normalize(supplied, Catalogue));
    }

    // The rule must not be so forgiving that it collides two capabilities.
    [Fact]
    public void OtherCapabilitiesResolveToThemselves()
    {
        Assert.Equal(
            "c3519eb0-f2f5-4fc2-b9ea-9249becbb964",
            CommandDispatcher.Normalize("SinkCleanliness", Catalogue));
    }
}
