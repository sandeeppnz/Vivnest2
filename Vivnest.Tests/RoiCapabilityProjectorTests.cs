using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Tests;

// ADR-108. Object Detection and Sink Cleanliness share
// RoiCapabilityRuntimeProjector, which is safe only because the one thing
// that genuinely differs between them is still expressed per capability.
//
// That difference is ExpectedClasses: Object Detection projects it onto
// the executing agent's document when set, Sink Cleanliness has no such
// setting. Before this file it had no test at all - the extraction would
// have silently dropped it, or silently given it to both, and everything
// else would still have been green.
public class RoiCapabilityProjectorTests
{
    private const string FullRoi =
        """
        {"RoiLeft":"10","RoiTop":"20","RoiRight":"30","RoiBottom":"40",
         "ModelPath":"Models/m.onnx","ConfidenceThreshold":"0.6"}
        """;

    private const string RoiWithExpectedClasses =
        """
        {"RoiLeft":"10","RoiTop":"20","RoiRight":"30","RoiBottom":"40",
         "ModelPath":"Models/m.onnx","ConfidenceThreshold":"0.6",
         "ExpectedClasses":"person,cup"}
        """;

    // ---- the capability-specific part -----------------------------------
    [Fact]
    public void ObjectDetectionProjectsExpectedClassesOntoTheExecutingAgent()
    {
        var result = new ObjectDetectionRuntimeProjector()
            .Project(Assignment(RoiWithExpectedClasses), Device(), "runtime-agent-1");

        Assert.Empty(result.Warnings);
        Assert.Equal("person,cup", result.AgentEntry!.Settings["ExpectedClasses"]);
    }

    // Absent rather than empty: the real ObjectDetectionModelOptions treats
    // "no restriction" as its own default, so an empty string would be a
    // different statement from saying nothing.
    [Fact]
    public void ObjectDetectionOmitsExpectedClassesWhenItIsNotSet()
    {
        var result = new ObjectDetectionRuntimeProjector()
            .Project(Assignment(FullRoi), Device(), "runtime-agent-1");

        Assert.Empty(result.Warnings);
        Assert.DoesNotContain("ExpectedClasses", result.AgentEntry!.Settings.Keys);
    }

    // The shared base must not hand Sink Cleanliness a setting that belongs
    // to Object Detection, even when the stored assignment happens to carry
    // one.
    [Fact]
    public void SinkCleanlinessNeverProjectsExpectedClasses()
    {
        var result = new SinkCleanlinessRuntimeProjector()
            .Project(Assignment(RoiWithExpectedClasses), Device(), "runtime-agent-1");

        Assert.Empty(result.Warnings);
        Assert.DoesNotContain("ExpectedClasses", result.AgentEntry!.Settings.Keys);
    }

    // ---- the shared part, proved to behave identically -------------------
    [Theory]
    [InlineData("Object Detection")]
    [InlineData("Sink Cleanliness")]
    public void BothWarnUnderTheirOwnNameAndPublishNeitherHalf(string capabilityName)
    {
        ICapabilityRuntimeProjector projector = capabilityName == "Object Detection"
            ? new ObjectDetectionRuntimeProjector()
            : new SinkCleanlinessRuntimeProjector();

        // ROI present, model parameters missing. One warnings list gates
        // both halves: a device with ROI but no model is broken at runtime,
        // so neither entry may publish.
        var result = projector.Project(
            Assignment("""{"RoiLeft":"10","RoiTop":"20","RoiRight":"30","RoiBottom":"40"}"""),
            Device(),
            "runtime-agent-1");

        Assert.NotEmpty(result.Warnings);
        Assert.All(result.Warnings, w => Assert.StartsWith(capabilityName + ":", w));

        Assert.Null(result.DeviceEntry);
        Assert.Null(result.AgentEntry);
    }

    [Theory]
    [InlineData("Object Detection")]
    [InlineData("Sink Cleanliness")]
    public void BothSplitRoiOntoTheDeviceAndModelOntoTheAgent(string capabilityName)
    {
        ICapabilityRuntimeProjector projector = capabilityName == "Object Detection"
            ? new ObjectDetectionRuntimeProjector()
            : new SinkCleanlinessRuntimeProjector();

        var result = projector.Project(Assignment(FullRoi), Device(), "runtime-agent-1");

        Assert.Empty(result.Warnings);

        // ROI is device-local; model parameters route to the executing agent.
        Assert.Equal("10", result.DeviceEntry!.Settings["RoiLeft"]);
        Assert.DoesNotContain("ModelPath", result.DeviceEntry.Settings.Keys);

        Assert.Equal("Models/m.onnx", result.AgentEntry!.Settings["ModelPath"]);
        Assert.DoesNotContain("RoiLeft", result.AgentEntry.Settings.Keys);
    }

    // =======================================================================

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
            ExecutingAgentId = "admin-agent-1",
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
            RuntimeDeviceId = "runtime-device-1"
        };
}
