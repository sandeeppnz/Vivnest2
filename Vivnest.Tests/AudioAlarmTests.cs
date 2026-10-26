using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Vivnest.Capabilities.Audio;
using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Cloud.Entities;
using Vivnest.Core.Configuration;
using Vivnest.Core.Devices.Audio;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Events;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;

namespace Vivnest.Tests;

// The detector is tested against synthesised audio rather than recordings:
// a tone's frequency, level and timing are exactly known, so each case
// states what it is and why it should or should not count as an alarm.
public class AudioAlarmTests
{
    private const int Rate = 16000;

    // ---- detector ---------------------------------------------------------

    [Fact]
    public void ThreeBeepsAtOnePitchAreAnAlarm()
    {
        var clip = Beeps(hz: 3100, count: 3, onSeconds: 0.5, offSeconds: 0.5, totalSeconds: 5);

        var result = new AlarmToneDetector().Analyse(clip);

        Assert.True(result.Detected);
        Assert.Equal(3, result.BurstCount);
        Assert.InRange(result.FrequencyHz!.Value, 3050, 3150);
    }

    [Fact]
    public void ABeepSurvivesRoomNoise()
    {
        var clip = Beeps(hz: 3100, count: 3, onSeconds: 0.5, offSeconds: 0.5, totalSeconds: 5, amplitude: 0.3);
        AddNoise(clip, amplitude: 0.05);

        Assert.True(new AlarmToneDetector().Analyse(clip).Detected);
    }

    [Fact]
    public void ASustainedToneIsAnAlarm()
    {
        var clip = Beeps(hz: 3100, count: 1, onSeconds: 4, offSeconds: 0, totalSeconds: 5);

        var result = new AlarmToneDetector().Analyse(clip);

        Assert.True(result.Detected);
        Assert.True(result.LongestTone >= TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void OneOrTwoBeepsAreNot()
    {
        // A microwave finishing, a doorbell: a short run that never
        // becomes the repeating pattern.
        var clip = Beeps(hz: 3100, count: 2, onSeconds: 0.5, offSeconds: 0.5, totalSeconds: 5);

        Assert.False(new AlarmToneDetector().Analyse(clip).Detected);
    }

    [Fact]
    public void ThreeDifferentNotesAreAMelodyNotAnAlarm()
    {
        var clip = new float[Rate * 5];
        AddTone(clip, 2600, startSeconds: 0.0, lengthSeconds: 0.5);
        AddTone(clip, 3100, startSeconds: 1.0, lengthSeconds: 0.5);
        AddTone(clip, 3800, startSeconds: 2.0, lengthSeconds: 0.5);

        Assert.False(new AlarmToneDetector().Analyse(new AudioClip(clip, Rate)).Detected);
    }

    [Fact]
    public void TonesOutsideTheAlarmBandAreIgnored()
    {
        var clip = Beeps(hz: 800, count: 3, onSeconds: 0.5, offSeconds: 0.5, totalSeconds: 5);

        Assert.False(new AlarmToneDetector().Analyse(clip).Detected);
    }

    [Fact]
    public void BroadbandNoiseIsNotAnAlarm()
    {
        var clip = new float[Rate * 5];
        AddNoise(clip, amplitude: 0.4);

        Assert.False(new AlarmToneDetector().Analyse(new AudioClip(clip, Rate)).Detected);
    }

    [Fact]
    public void SilenceIsNotAnAlarm()
    {
        Assert.False(new AlarmToneDetector().Analyse(new AudioClip(new float[Rate * 5], Rate)).Detected);
    }

    [Fact]
    public void AClipTooShortToAnalyseIsNotAnAlarm()
    {
        Assert.False(new AlarmToneDetector().Analyse(new AudioClip(new float[10], Rate)).Detected);
    }

    // ---- options ----------------------------------------------------------

    [Fact]
    public void UnsetOptionsFallBackRatherThanSpinning()
    {
        var options = new AudioAlarmOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.EffectiveInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.EffectiveClipDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), options.EffectiveCooldown);
    }

    [Fact]
    public void AnAbsurdClipLengthIsCapped()
    {
        var options = new AudioAlarmOptions { ClipDuration = TimeSpan.FromHours(1) };

        Assert.Equal(TimeSpan.FromSeconds(60), options.EffectiveClipDuration);
    }

    // ---- adapter ----------------------------------------------------------

    [Fact]
    public void TheAdapterWritesAudioAlarmOntoTheDevice()
    {
        var device = new JsonObject { ["DeviceId"] = "cam-1" };
        var entry = Entry(enabled: true, ("ScheduleIntervalSeconds", "45"), ("ClipSeconds", "8"), ("CooldownMinutes", "5"));

        new AudioAlarmRuntimeAdapter().Apply(device, entry);

        var audio = Assert.IsType<JsonObject>(device["AudioAlarm"]);
        Assert.Equal(TimeSpan.FromSeconds(45), TimeSpan.Parse(audio["Interval"]!.GetValue<string>()));
        Assert.Equal(TimeSpan.FromSeconds(8), TimeSpan.Parse(audio["ClipDuration"]!.GetValue<string>()));
        Assert.Equal(TimeSpan.FromMinutes(5), TimeSpan.Parse(audio["Cooldown"]!.GetValue<string>()));
    }

    [Fact]
    public void AnAssignmentWithNoSettingsStillListensOnDefaults()
    {
        var device = new JsonObject { ["DeviceId"] = "cam-1" };

        new AudioAlarmRuntimeAdapter().Apply(device, Entry(enabled: true));

        Assert.IsType<JsonObject>(device["AudioAlarm"]);
    }

    [Fact]
    public void ADisabledAssignmentWritesNothing()
    {
        var device = new JsonObject { ["DeviceId"] = "cam-1" };

        new AudioAlarmRuntimeAdapter().Apply(device, Entry(enabled: false, ("ScheduleIntervalSeconds", "45")));

        Assert.Null(device["AudioAlarm"]);
    }

    [Fact]
    public void ABadNumberIsSkippedNotFatal()
    {
        var device = new JsonObject { ["DeviceId"] = "cam-1" };

        new AudioAlarmRuntimeAdapter().Apply(device, Entry(enabled: true, ("ScheduleIntervalSeconds", "soon"), ("ClipSeconds", "8")));

        var audio = Assert.IsType<JsonObject>(device["AudioAlarm"]);
        Assert.Null(audio["Interval"]);
        Assert.NotNull(audio["ClipDuration"]);
    }

    [Fact]
    public void TheDeviceConfigPipelineAppliesTheAdapter()
    {
        var published = new JsonObject
        {
            ["SchemaVersion"] = Vivnest.Core.Constants.RuntimeConfigurationSchemaVersions.CurrentDeviceSchemaVersion,
            ["RuntimeDeviceId"] = "cam-1",
            ["Device"] = new JsonObject { ["Name"] = "Hall", ["Type"] = "Camera", ["Enabled"] = true },
            ["Capabilities"] = new JsonArray(Entry(enabled: true, ("ScheduleIntervalSeconds", "30"))),
        };

        var flattened = DeviceConfigRuntimeAdapter.Adapt(published);

        Assert.NotNull(flattened["AudioAlarm"]);
    }

    // ---- projector --------------------------------------------------------

    [Fact]
    public void TheProjectorAcceptsAnAssignmentWithNoSettings()
    {
        var result = Project("{}");

        Assert.NotNull(result.DeviceEntry);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void TheProjectorRejectsANonNumericSetting()
    {
        var result = Project("{\"ScheduleIntervalSeconds\":\"often\"}");

        Assert.Null(result.DeviceEntry);
        Assert.Contains(result.Warnings, w => w.Contains("ScheduleIntervalSeconds"));
    }

    [Fact]
    public void TheProjectorRejectsMalformedJson()
    {
        Assert.Null(Project("{not json").DeviceEntry);
    }

    // ---- PCM decoding -----------------------------------------------------

    [Fact]
    public void Pcm16DecodesLittleEndianAndDropsATrailingByte()
    {
        // 0x4000 = +0.5, 0xC000 = -0.5, then one stray byte.
        var samples = Vivnest.Infrastructure.Audio.RtspAudioClipSource.DecodePcm16([0x00, 0x40, 0x00, 0xC0, 0x7F]);

        Assert.Equal(2, samples.Length);
        Assert.Equal(0.5f, samples[0], 3);
        Assert.Equal(-0.5f, samples[1], 3);
    }

    // ---- worker -----------------------------------------------------------

    [Fact]
    public async Task ADetectionIsReportedOnceThenSilencedByTheCooldown()
    {
        var device = new DeviceOptions
        {
            DeviceId = "cam-1",
            Type = DeviceType.Camera,
            AudioAlarm = new AudioAlarmOptions
            {
                Interval = TimeSpan.FromMilliseconds(20),
                ClipDuration = TimeSpan.FromSeconds(1),
                Cooldown = TimeSpan.FromMinutes(10),
            },
        };

        var dispatcher = new RecordingDispatcher();
        var worker = new AudioAlarmWorker(
            new FakeSourceFactory(),
            new AlwaysDetects(),
            dispatcher,
            new FakeDeviceStore(device),
            NullLogger<AudioAlarmWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, dispatcher.Published);
    }

    [Fact]
    public async Task ACameraWithoutTheCapabilityIsNeverSampled()
    {
        var device = new DeviceOptions { DeviceId = "cam-1", Type = DeviceType.Camera };

        var factory = new FakeSourceFactory();
        var worker = new AudioAlarmWorker(
            factory,
            new AlwaysDetects(),
            new RecordingDispatcher(),
            new FakeDeviceStore(device),
            NullLogger<AudioAlarmWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, factory.Created);
    }

    // ---- helpers ----------------------------------------------------------

    private static AudioClip Beeps(
        double hz, int count, double onSeconds, double offSeconds, double totalSeconds, double amplitude = 0.5)
    {
        var samples = new float[(int)(Rate * totalSeconds)];

        for (var i = 0; i < count; i++)
            AddTone(samples, hz, i * (onSeconds + offSeconds), onSeconds, amplitude);

        return new AudioClip(samples, Rate);
    }

    private static void AddTone(
        float[] samples, double hz, double startSeconds, double lengthSeconds, double amplitude = 0.5)
    {
        var start = (int)(startSeconds * Rate);
        var length = (int)(lengthSeconds * Rate);

        for (var i = 0; i < length && start + i < samples.Length; i++)
            samples[start + i] += (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate));
    }

    private static void AddNoise(AudioClip clip, double amplitude) => AddNoise(clip.Samples, amplitude);

    private static void AddNoise(float[] samples, double amplitude)
    {
        var random = new Random(1234);

        for (var i = 0; i < samples.Length; i++)
            samples[i] += (float)(amplitude * (random.NextDouble() * 2 - 1));
    }

    private static JsonObject Entry(bool enabled, params (string Key, string Value)[] settings)
    {
        var settingsObject = new JsonObject();

        foreach (var (key, value) in settings)
            settingsObject[key] = value;

        return new JsonObject
        {
            ["Name"] = "Audio Alarm",
            ["Enabled"] = enabled,
            ["Settings"] = settingsObject,
        };
    }

    private static CapabilityProjectionResult Project(string settingsJson) =>
        new AudioAlarmRuntimeProjector().Project(
            new DeviceCapabilityEntity
            {
                TenantId = "t",
                SiteId = "s",
                CapabilityId = "audio.alarm",
                Enabled = true,
                Settings = settingsJson,
            },
            new DeviceRegistryEntity { TenantId = "t", SiteId = "s" },
            executingRuntimeAgentId: null);

    private sealed class AlwaysDetects : IAlarmToneDetector
    {
        public AlarmDetection Analyse(AudioClip clip) =>
            new(true, 3, 3100, TimeSpan.FromSeconds(1));
    }

    private sealed class FakeSourceFactory : IAudioClipSourceFactory
    {
        public int Created { get; private set; }

        public IAudioClipSource Create(DeviceOptions deviceOptions)
        {
            Created++;
            return new FakeSource();
        }
    }

    private sealed class FakeSource : IAudioClipSource
    {
        public Task<AudioClip> CaptureAsync(TimeSpan duration, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AudioClip(new float[Rate], Rate));
    }

    private sealed class RecordingDispatcher : IEventDispatcher
    {
        private int _published;

        public int Published => _published;

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _published);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceStore : IDeviceRuntimeStore
    {
        private readonly IReadOnlyList<DeviceOptions> _devices;

        public FakeDeviceStore(params DeviceOptions[] devices) => _devices = devices;

        public IReadOnlyCollection<DeviceOptions> GetDevices() => _devices;

        public IReadOnlyCollection<DeviceOptions> GetDevices(string id) =>
            _devices.Where(d => d.DeviceId == id).ToList();

        public DeviceOptions GetDevice(string id, DeviceType type) =>
            _devices.First(d => d.DeviceId == id && d.Type == type);
    }
}
