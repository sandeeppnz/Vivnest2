using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Vivnest.Core.Configuration;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Tests;

// The device-config blob at DeviceConfigBlob.BlobName holds one of two
// shapes, and both are live in the deployed container today: the legacy
// flat DeviceOptions shape (any device never republished since ADR-064),
// and the capabilities[] wire document the publisher now writes to both
// the versioned blob and the flat name.
//
// Deserializing the second one straight into DeviceOptions binds almost
// nothing, which is exactly the bug that made GET /devices/{id}/capabilities
// return a blank name and a Type defaulted to Camera for every republished
// device. These fixtures are trimmed copies of real blobs, kept in-repo so
// the test does not depend on a developer machine's config-cache.
public class DeviceConfigRuntimeAdapterTests
{
    private static readonly JsonSerializerOptions DeviceJson = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string OwningAgent = "5d6c8d6f-4b8d-47e0-a56f-3c3e8cdb2d63";

    private const string LegacyShape = $$"""
    {
      "DeviceId": "f7756a79-e507-4113-9b76-a9462b80a25d",
      "Name": "Tapo C120 Camera",
      "Type": "Camera",
      "Enabled": true,
      "Brand": "TP-Link",
      "Model": "C120",
      "Location": "Kitchen",
      "OwningAgentId": "{{OwningAgent}}",
      "Settings": { "Host": "192.168.50.166", "Username": "admin" }
    }
    """;

    private const string NewShape = $$"""
    {
      "RuntimeDeviceId": "55cc8aa6-dc4f-4cfc-a71d-36a46935650e",
      "Device": {
        "Name": "Kitchen Camera",
        "Type": "Camera",
        "Enabled": true,
        "Brand": "TP-Link",
        "Model": "C120",
        "Location": "Kitchen",
        "Firmware": "1.0",
        "Connection": { "Host": "192.168.50.166", "Username": "admin" }
      },
      "OwningAgentId": "{{OwningAgent}}",
      "Capabilities": [
        {
          "CapabilityId": "cap-1",
          "Name": "Image Capture",
          "Enabled": true,
          "ExecutingAgentId": null,
          "Settings": { "ScheduleInterval": "00:05:00", "LivenessInterval": "00:10:00" }
        }
      ],
      "PublishedUtc": "2026-08-15T00:00:00Z",
      "SchemaVersion": 1,
      "ConfigurationVersion": 3,
      "ConfigurationHash": "abc123"
    }
    """;

    private static DeviceOptions Adapt(string json)
    {
        var raw = (JsonObject)JsonNode.Parse(json)!;
        return DeviceConfigRuntimeAdapter.Adapt(raw).Deserialize<DeviceOptions>(DeviceJson)!;
    }

    [Fact]
    public void LegacyShapePassesThroughUnchanged()
    {
        var device = Adapt(LegacyShape);

        Assert.Equal("Tapo C120 Camera", device.Name);
        Assert.Equal(DeviceType.Camera, device.Type);
        Assert.True(device.Enabled);
        Assert.Equal("192.168.50.166", device.Settings.Host);
        Assert.Equal(OwningAgent, device.OwningAgentId);
    }

    // The regression this exists for: without Adapt, every one of these
    // assertions fails - Name blank, Type defaulted to Camera (enum 0)
    // whatever the device really is, Enabled false, Settings empty.
    [Fact]
    public void NewShapeIsFlattenedRatherThanSilentlyLosingEveryField()
    {
        var device = Adapt(NewShape);

        Assert.Equal("Kitchen Camera", device.Name);
        Assert.Equal(DeviceType.Camera, device.Type);
        Assert.True(device.Enabled);
        Assert.Equal("192.168.50.166", device.Settings.Host);
        Assert.Equal(OwningAgent, device.OwningAgentId);
        Assert.Equal("55cc8aa6-dc4f-4cfc-a71d-36a46935650e", device.DeviceId);
    }

    [Fact]
    public void NewShapeBoundDirectlyToDeviceOptionsLosesEverything()
    {
        // Documents the failure mode, so nobody "simplifies" Adapt away.
        var direct = JsonSerializer.Deserialize<DeviceOptions>(NewShape, DeviceJson)!;

        Assert.Equal(string.Empty, direct.Name);
        Assert.False(direct.Enabled);
        Assert.Equal(string.Empty, direct.Settings.Host);
    }

    // TryProcessDeviceBlob checks ownership on the RAW document, before
    // decrypting, so that an Agent never decrypts another tenant's
    // credentials. That is only safe while pre-adapt and post-adapt
    // ownership can never disagree.
    [Theory]
    [InlineData(nameof(LegacyShape))]
    [InlineData(nameof(NewShape))]
    public void OwnershipIsIdenticalBeforeAndAfterAdapting(string fixtureName)
    {
        var json = fixtureName == nameof(LegacyShape) ? LegacyShape : NewShape;

        var beforeAdapt = ((JsonObject)JsonNode.Parse(json)!)["OwningAgentId"]?.GetValue<string>();
        var afterAdapt = DeviceConfigRuntimeAdapter
            .Adapt((JsonObject)JsonNode.Parse(json)!)["OwningAgentId"]?.GetValue<string>();

        Assert.False(string.IsNullOrWhiteSpace(beforeAdapt));
        Assert.Equal(beforeAdapt, afterAdapt);
    }

    [Fact]
    public void UnrecognisedSchemaVersionIsRejectedRatherThanGuessedAt()
    {
        var raw = (JsonObject)JsonNode.Parse(NewShape)!;
        raw["SchemaVersion"] = 99;

        Assert.Throws<UnsupportedConfigurationSchemaException>(
            () => DeviceConfigRuntimeAdapter.Adapt(raw));
    }
}
