using Vivnest.Cloud.Admin;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Tests;

// ADR-100. "Required" means resolvable after defaults, not "must be
// stored". These cover the projection-time pass, which is what makes that
// true for rows the write-time pass never touched - a schema field added
// after an assignment was written, or settings edited outside the admin
// API.
public class CapabilityDefaultsTests
{
    private const string Schema = """
    [
      {"Name":"ScheduleIntervalSeconds","Type":"Number","Required":true,"Minimum":1,"Maximum":null,"AllowedValues":null,"DefaultValue":"900"},
      {"Name":"BurstIntervalSeconds","Type":"Number","Required":true,"Minimum":1,"Maximum":null,"AllowedValues":null,"DefaultValue":"30"},
      {"Name":"NoDefaultHere","Type":"Number","Required":false,"Minimum":null,"Maximum":null,"AllowedValues":null,"DefaultValue":null}
    ]
    """;

    private static CapabilityEntity Capability(
        string? schema = Schema,
        string? defaults = """{"ScheduleIntervalSeconds":"600"}""") =>
        new()
        {
            PartitionKey = "capability",
            RowKey = "cap-1",
            CapabilityName = "Image Capture",
            CapabilityKey = "camera.capture",
            CapabilityType = "Device",
            Status = "Active",
            ConfigurationSchema = schema!,
            DefaultConfiguration = defaults!
        };

    private static CapabilityConfigurationService Service() => new();

    // The precedence rule, stated as a test: an explicit value is never
    // replaced by a default, however the default was declared.
    [Fact]
    public void SuppliedValuesAreNeverOverwritten()
    {
        var effective = Service().ResolveEffectiveSettings(
            Capability(),
            new Dictionary<string, string> { ["ScheduleIntervalSeconds"] = "30" });

        Assert.Equal("30", effective["ScheduleIntervalSeconds"]);
    }

    // Capability-level DefaultConfiguration wins over the field's own
    // DefaultValue - the catalogue's per-capability answer is more specific
    // than the schema's generic one.
    [Fact]
    public void CapabilityDefaultConfigurationBeatsTheFieldDefault()
    {
        var effective = Service().ResolveEffectiveSettings(
            Capability(),
            new Dictionary<string, string>());

        // DefaultConfiguration says 600, the field says 900.
        Assert.Equal("600", effective["ScheduleIntervalSeconds"]);

        // No DefaultConfiguration entry, so the field's own default applies.
        Assert.Equal("30", effective["BurstIntervalSeconds"]);
    }

    // A field with neither kind of default stays absent - defaulting must
    // not invent values, only supply declared ones.
    [Fact]
    public void AFieldWithNoDefaultStaysAbsent()
    {
        var effective = Service().ResolveEffectiveSettings(
            Capability(),
            new Dictionary<string, string>());

        Assert.False(effective.ContainsKey("NoDefaultHere"));
    }

    // Keys the schema does not declare are carried through untouched.
    // The schema describes what a capability needs, not an allow-list.
    [Fact]
    public void UnknownStoredKeysSurvive()
    {
        var effective = Service().ResolveEffectiveSettings(
            Capability(),
            new Dictionary<string, string> { ["SomethingElse"] = "keep me" });

        Assert.Equal("keep me", effective["SomethingElse"]);
    }

    // Applying twice must equal applying once - the projection-time pass
    // runs on rows the write-time pass already defaulted.
    [Fact]
    public void ApplyingDefaultsIsIdempotent()
    {
        var svc = Service();
        var once = svc.ResolveEffectiveSettings(Capability(), new Dictionary<string, string>());
        var twice = svc.ResolveEffectiveSettings(Capability(), once);

        Assert.Equal(once.OrderBy(x => x.Key), twice.OrderBy(x => x.Key));
    }

    // Broken reference data must not fail a publish that this device's own
    // assignment did nothing to cause.
    [Theory]
    [InlineData("not json", """{"ScheduleIntervalSeconds":"600"}""")]
    [InlineData(Schema, "not json")]
    public void MalformedSchemaOrDefaultsDegradeToNoDefaults(string schema, string defaults)
    {
        var effective = Service().ResolveEffectiveSettings(
            Capability(schema, defaults),
            new Dictionary<string, string> { ["BurstDurationSeconds"] = "600" });

        Assert.Equal("600", effective["BurstDurationSeconds"]);
    }
}
