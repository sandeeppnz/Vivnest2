using Vivnest.Core.Configuration;
using Vivnest.Core.Enums;

namespace Vivnest.Tests;

// Admin master data is free text ("Motion Sensor") while the code it binds
// to uses identifier spelling (DeviceType.MotionSensor). The bridge is one
// rule - strip spaces, compare OrdinalIgnoreCase - and when it fails to
// match, the failure mode is not an exception: the capability quietly drops
// out of the published document with only a warning.
public class RuntimeNameMatchTests
{
    private static readonly string[] Registered =
        ["Image Capture", "Object Detection", "Sink Cleanliness", "Motion Detection"];

    [Theory]
    [InlineData("Image Capture", "Image Capture")]
    [InlineData("ImageCapture", "Image Capture")]
    [InlineData("image capture", "Image Capture")]
    [InlineData("  Sink   Cleanliness  ", "Sink Cleanliness")]
    [InlineData("SINKCLEANLINESS", "Sink Cleanliness")]
    public void MatchesIgnoringSpacingAndCase(string input, string expected)
    {
        Assert.Equal(expected, RuntimeNameMatch.Find(Registered, input, x => x));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown Capability")]
    [InlineData("Image-Capture")]
    public void DoesNotMatchAnythingElse(string input)
    {
        Assert.Null(RuntimeNameMatch.Find(Registered, input, x => x));
    }

    [Theory]
    [InlineData("Motion Sensor", DeviceType.MotionSensor)]
    [InlineData("motion sensor", DeviceType.MotionSensor)]
    [InlineData("MOTIONSENSOR", DeviceType.MotionSensor)]
    [InlineData("Camera", DeviceType.Camera)]
    [InlineData("Smart Plug", DeviceType.SmartPlug)]
    public void ResolvesAdminDeviceTypeNamesToTheRuntimeEnum(string name, DeviceType expected)
    {
        Assert.Equal(expected, RuntimeNameMatch.ToDeviceType(name));
    }

    // Matched against Enum.GetNames rather than Enum.TryParse on purpose:
    // TryParse also accepts the underlying numeric value, so an admin
    // DeviceTypeName of "0" would silently resolve to Camera.
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData("Nonexistent")]
    public void NumericAndUnknownNamesResolveToNothing(string name)
    {
        Assert.Null(RuntimeNameMatch.ToDeviceType(name));
    }
}
