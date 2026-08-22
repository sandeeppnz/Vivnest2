using Microsoft.Extensions.Logging;
using Vivnest.Abstraction.Agent.Capabilities;

namespace Vivnest.Agent.Capabilities.Camera;

public sealed class CameraCapability : ICapability
{
    private readonly CameraCaptureWorker _worker;
    private readonly ILogger<CameraCapability> _logger;

    private CapabilityStatus _status = CapabilityStatus.Registered;

    public CapabilityStatus Status => _status;

    // No IRuntimeCapabilityAssignmentStore dependency, deliberately.
    // camera.capture consumes no Agent-level setting: capture cadence is
    // per-device configuration and already flows through
    // DeviceCapability.Settings -> ImageCaptureRuntimeProjector ->
    // ImageCaptureRuntimeAdapter -> DeviceOptions.Schedule.Interval, which
    // lets three cameras on one Agent keep three different schedules. See
    // decision-log.md ADR-097 (5G.11).
    public CameraCapability(
        CameraCaptureWorker worker,
        ILogger<CameraCapability> logger)
    {
        _worker = worker;
        _logger = logger;
    }

    public CapabilityManifest Manifest =>
        new()
        {
            Id = "camera.capture",
            Name = "Camera Capture",
            Version = "1.0.0",

            Commands =
            [
                new CapabilityCommandDescriptor(
                "camera.capture",
                "1.0")
            ],

            ProducedEvents =
            [
                new CapabilityEventDescriptor(
                "camera.capture.completed",
                "1.0"),

            new CapabilityEventDescriptor(
                "camera.capture.failed",
                "1.0")
            ],

            ConsumedEvents = [],

            Dependencies = []
        };

    public async Task StartAsync(
        ICapabilityContext context,
        CancellationToken cancellationToken)
    {
        if (_status is
            CapabilityStatus.Starting or
            CapabilityStatus.Running)
        {
            return;
        }

        _status = CapabilityStatus.Starting;

        try
        {
            // Observes its assignment; consumes nothing from it. camera.capture
            // has no Agent-level setting (ADR-101) - capture cadence is
            // per-device and lives on DeviceCapability. Logged so the
            // assignment actually reaching the right capability is visible
            // in a live Agent, not merely asserted in a test.
            _logger.LogInformation(
                "Starting capability {CapabilityId} for agent {AgentId}. " +
                "Assignment={AssignmentCapabilityId}, Enabled={Enabled}, " +
                "SettingsCount={SettingsCount}.",
                Manifest.Id,
                context.AgentId,
                context.Assignment.CapabilityId,
                context.Assignment.Enabled,
                context.Assignment.Settings.Count);

            await _worker.StartAsync(
                cancellationToken);

            _status = CapabilityStatus.Running;

            _logger.LogInformation(
                "Capability {CapabilityId} started.",
                Manifest.Id);
        }
        catch
        {
            _status = CapabilityStatus.Failed;

            throw;
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken)
    {
        if (_status is
            CapabilityStatus.Stopped or
            CapabilityStatus.Registered)
        {
            return;
        }

        _status = CapabilityStatus.Stopping;

        try
        {
            _logger.LogInformation(
                "Stopping capability {CapabilityId}.",
                Manifest.Id);

            await _worker.StopAsync(
                cancellationToken);

            _status = CapabilityStatus.Stopped;

            _logger.LogInformation(
                "Capability {CapabilityId} stopped.",
                Manifest.Id);
        }
        catch
        {
            _status = CapabilityStatus.Failed;

            throw;
        }
    }
}