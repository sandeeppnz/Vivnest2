using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vivnest.Core.Devices.Audio;
using Vivnest.Core.Events;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;

namespace Vivnest.Capabilities.Audio;

// One loop per camera that has the audio.alarm capability assigned: take a
// short clip off the audio track, look for an alarm tone, and publish
// AudioAlarmDetectedEvent when one is found.
//
// Deliberately its own worker rather than a branch inside
// CameraCaptureWorker. The snapshot loop has its own cadence, burst mode and
// failure accounting; sharing a loop would let a camera with no microphone
// look like a camera that cannot take pictures.
//
// Writes no DeviceRuntimeState for the same reason: LastActivityUtc and
// LastError belong to the snapshot loop and feed offline detection, and an
// audio failure is not the camera being offline.
public sealed class AudioAlarmWorker : BackgroundService
{
    // A device with no audio track fails identically on every clip. Logging
    // each one is 120 warnings an hour that say nothing new, so after the
    // first the failure is repeated only this often.
    private const int RepeatFailureLogEvery = 20;

    private readonly IAudioClipSourceFactory _sources;
    private readonly IAlarmToneDetector _detector;
    private readonly IEventDispatcher _dispatcher;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly ILogger<AudioAlarmWorker> _logger;

    public AudioAlarmWorker(
        IAudioClipSourceFactory sources,
        IAlarmToneDetector detector,
        IEventDispatcher dispatcher,
        IDeviceRuntimeStore deviceRegistry,
        ILogger<AudioAlarmWorker> logger)
    {
        _sources = sources;
        _detector = detector;
        _dispatcher = dispatcher;
        _deviceRegistry = deviceRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Audio Alarm Worker started.");

        var devices = _deviceRegistry.GetDevices()
            .Where(d => d.Type == DeviceType.Camera && d.AudioAlarm is not null)
            .ToList();

        if (devices.Count == 0)
        {
            _logger.LogWarning(
                "No camera has audio.alarm assigned. Audio Alarm Worker has nothing to do.");

            return;
        }

        await Task.WhenAll(devices.Select(d => RunLoopAsync(d, stoppingToken)));
    }

    private async Task RunLoopAsync(
        DeviceOptions device,
        CancellationToken stoppingToken)
    {
        var audio = device.AudioAlarm!;
        var source = _sources.Create(device);

        DateTime? lastAlertUtc = null;
        var consecutiveFailures = 0;

        _logger.LogInformation(
            "Listening for alarms on {DeviceId}: {Clip} clip every {Interval}, {Cooldown} between alerts.",
            device.DeviceId,
            audio.EffectiveClipDuration,
            audio.EffectiveInterval,
            audio.EffectiveCooldown);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var clip = await source.CaptureAsync(
                    audio.EffectiveClipDuration,
                    stoppingToken);

                if (consecutiveFailures > 0)
                {
                    _logger.LogInformation(
                        "Audio capture for {DeviceId} recovered after {Failures} failure(s).",
                        device.DeviceId,
                        consecutiveFailures);

                    consecutiveFailures = 0;
                }

                var detection = _detector.Analyse(clip);

                if (detection.Detected)
                {
                    lastAlertUtc = await AlertAsync(
                        device,
                        audio,
                        detection,
                        lastAlertUtc,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;

                if (consecutiveFailures == 1 || consecutiveFailures % RepeatFailureLogEvery == 0)
                {
                    _logger.LogWarning(
                        ex,
                        "Audio capture failed for {DeviceId} ({Failures} in a row).",
                        device.DeviceId,
                        consecutiveFailures);
                }
            }

            try
            {
                await Task.Delay(audio.EffectiveInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Returns the time of the alert last sent: now if one went out, the
    // previous value if the cooldown suppressed this one.
    private async Task<DateTime?> AlertAsync(
        DeviceOptions device,
        AudioAlarmOptions audio,
        AlarmDetection detection,
        DateTime? lastAlertUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        if (lastAlertUtc is { } last && now - last < audio.EffectiveCooldown)
        {
            _logger.LogInformation(
                "Alarm tone still present on {DeviceId}; alert suppressed ({Elapsed:g} into a {Cooldown} cooldown).",
                device.DeviceId,
                now - last,
                audio.EffectiveCooldown);

            return lastAlertUtc;
        }

        _logger.LogWarning(
            "Alarm tone detected on {DeviceId}: {Bursts} burst(s) around {Frequency:0} Hz.",
            device.DeviceId,
            detection.BurstCount,
            detection.FrequencyHz);

        try
        {
            await _dispatcher.PublishAsync(
                new AudioAlarmDetectedEvent(
                    device.DeviceId,
                    now,
                    detection.FrequencyHz,
                    detection.BurstCount,
                    detection.LongestTone),
                cancellationToken);

            return now;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Not stamped as sent: if reporting failed, the next clip should
            // try again rather than sit out a cooldown for an alert nobody
            // received.
            _logger.LogError(
                ex,
                "Alarm detected on {DeviceId} but reporting it failed.",
                device.DeviceId);

            return lastAlertUtc;
        }
    }
}
