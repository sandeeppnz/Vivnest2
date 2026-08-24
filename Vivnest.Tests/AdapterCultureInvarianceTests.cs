using System.Globalization;
using System.Text.Json.Nodes;
using Vivnest.Core.Configuration;

namespace Vivnest.Tests;

// The config adapters parse numeric wire values out of published blobs.
// Until 2026-08-24 they did it with the culture-sensitive TryParse
// overloads, which on a comma-decimal host is not a skip-on-mismatch
// hazard but a silent-corruption one: under de-DE, "." is the GROUP
// separator, so double.TryParse("1.5") SUCCEEDS and returns fifteen. A
// burst interval of 1.5 seconds became 15 with no warning, because
// nothing failed.
//
// EventRowKey in the same assembly documents this exact class of bug and
// why InvariantCulture is load-bearing for wire/storage values. Same
// argument here; these tests pin it.
//
// Each test saves and restores the thread culture rather than trusting
// xunit's isolation, so a failure cannot leak de-DE into other tests.
public class AdapterCultureInvarianceTests
{
    [Fact]
    public void AFractionalSecondsValueMeansTheSameThingUnderACommaDecimalCulture()
    {
        RunUnder(new CultureInfo("de-DE"), () =>
        {
            var device = FlattenedDevice();

            new ImageCaptureRuntimeAdapter().Apply(
                device,
                CapabilityEntry("BurstIntervalSeconds", "1.5"));

            // Under CurrentCulture parsing this was 15 seconds - "." read
            // as a group separator, and the parse succeeded.
            var burst = device["Schedule"]!["Burst"]!["Interval"]!.GetValue<string>();

            Assert.Equal(TimeSpan.FromSeconds(1.5).ToString(), burst);
        });
    }

    [Fact]
    public void ACommaDecimalValueIsRejectedNotReadAsAThousandsNumber()
    {
        RunUnder(new CultureInfo("de-DE"), () =>
        {
            var device = FlattenedDevice();

            // "1,5" is what a de-DE-cultured WRITER would have produced.
            // The wire format is invariant, so this is malformed and must
            // be skipped - not parsed as the host culture's 1.5.
            new ImageCaptureRuntimeAdapter().Apply(
                device,
                CapabilityEntry("ScheduleIntervalSeconds", "1,5"));

            Assert.Null(device["Schedule"]);
        });
    }

    [Fact]
    public void MotionDetectionMinutesParseIdenticallyUnderAnyCulture()
    {
        RunUnder(new CultureInfo("de-DE"), () =>
        {
            var device = FlattenedDevice();

            new MotionDetectionRuntimeAdapter().Apply(
                device,
                CapabilityEntry("BatteryReportIntervalMinutes", "2.5"));

            var interval = device["Schedule"]!["Interval"]!.GetValue<string>();

            Assert.Equal(TimeSpan.FromMinutes(2.5).ToString(), interval);
        });
    }

    [Fact]
    public void RoiIntegersStillParseUnderACommaDecimalCulture()
    {
        RunUnder(new CultureInfo("de-DE"), () =>
        {
            var device = FlattenedDevice();

            var entry = new JsonObject
            {
                ["Enabled"] = true,
                ["ExecutingAgentId"] = "agent-ai",
                ["Settings"] = new JsonObject
                {
                    ["RoiLeft"] = "10",
                    ["RoiTop"] = "20",
                    ["RoiRight"] = "30",
                    ["RoiBottom"] = "40"
                }
            };

            new ObjectDetectionRuntimeAdapter().Apply(device, entry);

            Assert.Equal(10, device["ObjectDetection"]!["RoiLeft"]!.GetValue<int>());
            Assert.Equal(40, device["ObjectDetection"]!["RoiBottom"]!.GetValue<int>());
        });
    }

    // =======================================================================

    private static JsonObject FlattenedDevice() => new()
    {
        ["DeviceId"] = "camera-0"
    };

    private static JsonObject CapabilityEntry(string key, string value) => new()
    {
        ["Settings"] = new JsonObject { [key] = value }
    };

    private static void RunUnder(CultureInfo culture, Action test)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;

            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
