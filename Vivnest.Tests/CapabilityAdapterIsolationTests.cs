using System.Text.Json.Nodes;
using Vivnest.Core.Configuration;

namespace Vivnest.Tests;

// ADR-099. Two capability adapters used to write the same two root fields
// on a device - LivenessInterval and WarningMultiplier - so a device
// carrying both Image Capture and Motion Detection got whichever value the
// later capabilities[] entry supplied. The same configuration produced
// different runtime behaviour depending on array order, which is the
// failure this file exists to make impossible to reintroduce quietly.
//
// The rule these tests encode: a capability adapter writes only what that
// capability owns. Device liveness policy belongs to the device.
public class CapabilityAdapterIsolationTests
{
    private static JsonObject Device() =>
        new()
        {
            ["DeviceId"] = "device-1",
            // Projected from the device row, not from any capability.
            ["LivenessInterval"] = "00:05:00",
            ["WarningMultiplier"] = 3.0
        };

    private static JsonObject Entry(string capabilityName, params (string Key, string Value)[] settings)
    {
        var s = new JsonObject();

        foreach (var (key, value) in settings)
            s[key] = value;

        return new JsonObject
        {
            ["CapabilityName"] = capabilityName,
            ["Settings"] = s
        };
    }

    // The exact collision, in the order that used to let Motion Detection win.
    [Fact]
    public void NeitherAdapterOverwritesDeviceLiveness()
    {
        var device = Device();

        new ImageCaptureRuntimeAdapter().Apply(
            device,
            Entry("Image Capture",
                ("ScheduleIntervalSeconds", "900"),
                ("LivenessIntervalSeconds", "60"),
                ("WarningMultiplier", "9")));

        new MotionDetectionRuntimeAdapter().Apply(
            device,
            Entry("Motion Detection",
                ("BatteryReportIntervalMinutes", "30"),
                ("LivenessIntervalMinutes", "45"),
                ("WarningMultiplier", "7")));

        Assert.Equal("00:05:00", device["LivenessInterval"]!.GetValue<string>());
        Assert.Equal(3.0, device["WarningMultiplier"]!.GetValue<double>());
    }

    // ...and in the reverse order. Order-independence is the actual
    // property being asserted; one ordering passing proves nothing.
    [Fact]
    public void TheResultIsIndependentOfCapabilityOrder()
    {
        var motionFirst = Device();

        new MotionDetectionRuntimeAdapter().Apply(
            motionFirst,
            Entry("Motion Detection", ("LivenessIntervalMinutes", "45"), ("WarningMultiplier", "7")));
        new ImageCaptureRuntimeAdapter().Apply(
            motionFirst,
            Entry("Image Capture", ("LivenessIntervalSeconds", "60"), ("WarningMultiplier", "9")));

        var captureFirst = Device();

        new ImageCaptureRuntimeAdapter().Apply(
            captureFirst,
            Entry("Image Capture", ("LivenessIntervalSeconds", "60"), ("WarningMultiplier", "9")));
        new MotionDetectionRuntimeAdapter().Apply(
            captureFirst,
            Entry("Motion Detection", ("LivenessIntervalMinutes", "45"), ("WarningMultiplier", "7")));

        Assert.Equal(
            motionFirst["LivenessInterval"]!.GetValue<string>(),
            captureFirst["LivenessInterval"]!.GetValue<string>());

        Assert.Equal(
            motionFirst["WarningMultiplier"]!.GetValue<double>(),
            captureFirst["WarningMultiplier"]!.GetValue<double>());
    }

    // The adapters must still do their real job - this is not passing
    // because both were reduced to no-ops.
    [Fact]
    public void EachAdapterStillWritesWhatItOwns()
    {
        var device = Device();

        new ImageCaptureRuntimeAdapter().Apply(
            device,
            Entry("Image Capture", ("ScheduleIntervalSeconds", "900")));

        Assert.Equal(
            "00:15:00",
            device["Schedule"]!.AsObject()["Interval"]!.GetValue<string>());

        var sensor = Device();

        new MotionDetectionRuntimeAdapter().Apply(
            sensor,
            Entry("Motion Detection", ("BatteryReportIntervalMinutes", "30")));

        Assert.Equal(
            "00:30:00",
            sensor["Schedule"]!.AsObject()["Interval"]!.GetValue<string>());
    }

    // The device section carries seconds; DeviceOptions holds a TimeSpan.
    // Zero must mean "unset" and leave the runtime default alone - clamping
    // liveness to zero would make every device instantly stale.
    [Theory]
    [InlineData(300, 3.0, "00:05:00", 3.0)]
    [InlineData(0, 0.0, null, null)]
    public void DeviceOwnedLivenessIsConvertedAndZeroMeansUnset(
        int seconds, double multiplier, string? expectedInterval, double? expectedMultiplier)
    {
        var deviceSection = new JsonObject
        {
            ["DeviceId"] = "device-1",
            ["Name"] = "Kitchen Camera",
            ["LivenessIntervalSeconds"] = seconds,
            ["WarningMultiplier"] = multiplier
        };

        var raw = new JsonObject
        {
            ["Device"] = deviceSection,
            ["Capabilities"] = new JsonArray()
        };

        var flattened = DeviceConfigRuntimeAdapter.Adapt(raw);

        if (expectedInterval == null)
            Assert.Null(flattened["LivenessInterval"]);
        else
            Assert.Equal(expectedInterval, flattened["LivenessInterval"]!.GetValue<string>());

        if (expectedMultiplier == null)
            Assert.Null(flattened["WarningMultiplier"]);
        else
            Assert.Equal(expectedMultiplier.Value, flattened["WarningMultiplier"]!.GetValue<double>());
    }

    // ObjectDetection and SinkCleanliness were already safe - they write
    // into their own named sub-objects. Asserted so the pattern that made
    // them safe is not lost when someone edits them.
    //
    // Note these adapters write ROI and ExecutingAgentId, not ModelPath:
    // model parameters are an AGENT-side contribution, projected onto the
    // executing agent's own document rather than the device's. Two ROIs on
    // one device is precisely the case that would collide if either wrote
    // to the root.
    [Fact]
    public void ModelCapabilitiesWriteOnlyTheirOwnSubObject()
    {
        var device = Device();

        new SinkCleanlinessRuntimeAdapter().Apply(
            device,
            Entry("Sink Cleanliness",
                ("RoiLeft", "1"), ("RoiTop", "2"), ("RoiRight", "3"), ("RoiBottom", "4")));

        new ObjectDetectionRuntimeAdapter().Apply(
            device,
            Entry("Object Detection",
                ("RoiLeft", "5"), ("RoiTop", "6"), ("RoiRight", "7"), ("RoiBottom", "8")));

        Assert.Equal(1, device["SinkCleanliness"]!.AsObject()["RoiLeft"]!.GetValue<int>());
        Assert.Equal(5, device["ObjectDetection"]!.AsObject()["RoiLeft"]!.GetValue<int>());

        // Neither leaked a ROI onto the device root, and neither touched
        // the other's block or the device's liveness policy.
        Assert.Null(device["RoiLeft"]);
        Assert.Equal("00:05:00", device["LivenessInterval"]!.GetValue<string>());
    }
}
