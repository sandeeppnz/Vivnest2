using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Tests;

// ADR-099 / 5H.7, the Cloud-side half of the protection.
//
// CapabilityAdapterIsolationTests proves an adapter cannot OVERWRITE device
// liveness at runtime. This proves the projector cannot EMIT it in the
// first place - so a stale key in a stored assignment cannot reappear on
// the wire looking like configuration.
//
// The two together:
//     Cloud projection  -> cannot emit device-owned liveness
//     Runtime adapter   -> cannot overwrite device-owned liveness
public class CapabilityProjectionOwnershipTests
{
    private static DeviceCapabilityEntity Assignment(string settingsJson) =>
        new()
        {
            PartitionKey = "tenant-1|site-1",
            RowKey = "assignment-1",
            TenantId = "tenant-1",
            SiteId = "site-1",
            DeviceId = "device-1",
            CapabilityId = "cap-1",
            Status = "Active",
            Enabled = true,
            ExecutingAgentId = "",
            Settings = settingsJson
        };

    private static DeviceRegistryEntity Device() =>
        new()
        {
            PartitionKey = "tenant-1|site-1",
            RowKey = "device-1",
            TenantId = "tenant-1",
            SiteId = "site-1",
            Name = "Kitchen Camera",
            Status = "Active",
            // Device-owned (ADR-099) - the projector must take liveness
            // from here, and capabilities must never contribute to it.
            LivenessIntervalSeconds = 300,
            WarningMultiplier = 3
        };

    // A stored assignment that still carries the old keys - exactly the
    // rows that exist in live storage today. They must be ignored, not
    // forwarded, and their presence must not fail the projection either:
    // refusing to publish over stale data would take working devices down.
    [Fact]
    public void ImageCaptureDoesNotEmitDeviceOwnedLivenessEvenWhenStored()
    {
        var result = new ImageCaptureRuntimeProjector().Project(
            Assignment("""
            {
              "ScheduleIntervalSeconds": "900",
              "BurstIntervalSeconds": "30",
              "BurstDurationSeconds": "600",
              "LivenessIntervalSeconds": "60",
              "WarningMultiplier": "9"
            }
            """),
            Device(),
            executingRuntimeAgentId: null);

        Assert.Empty(result.Warnings);
        Assert.NotNull(result.DeviceEntry);

        var settings = result.DeviceEntry!.Settings;

        Assert.False(settings.ContainsKey("LivenessIntervalSeconds"));
        Assert.False(settings.ContainsKey("WarningMultiplier"));

        // ...while still projecting what Image Capture does own.
        Assert.Equal("900", settings["ScheduleIntervalSeconds"]);
        Assert.Equal("30", settings["BurstIntervalSeconds"]);
        Assert.Equal("600", settings["BurstDurationSeconds"]);
    }

    // The keys are no longer required, so an assignment carrying only the
    // capability's own settings projects cleanly. Before 5H.7 this would
    // have produced two "is not set" warnings and blocked the publish.
    [Fact]
    public void ImageCapturePublishesWithoutTheOldLivenessKeys()
    {
        var result = new ImageCaptureRuntimeProjector().Project(
            Assignment("""
            {
              "ScheduleIntervalSeconds": "900",
              "BurstIntervalSeconds": "30",
              "BurstDurationSeconds": "600"
            }
            """),
            Device(),
            executingRuntimeAgentId: null);

        Assert.Empty(result.Warnings);
        Assert.NotNull(result.DeviceEntry);
        Assert.Equal(3, result.DeviceEntry!.Settings.Count);
    }

    // Capture cadence genuinely is per-device and stays required - this is
    // the distinction 5H exists to hold. Removing liveness must not have
    // relaxed the capability's own configuration.
    [Fact]
    public void ImageCaptureStillRequiresItsOwnCadenceSettings()
    {
        var result = new ImageCaptureRuntimeProjector().Project(
            Assignment("""{ "BurstIntervalSeconds": "30" }"""),
            Device(),
            executingRuntimeAgentId: null);

        Assert.Null(result.DeviceEntry);
        Assert.Contains(result.Warnings, w => w.Contains("ScheduleIntervalSeconds", StringComparison.Ordinal));
    }
}
