using Microsoft.Extensions.Logging;
using Vivnest.Abstraction.Agent.Capabilities;

namespace Vivnest.Agent.Capabilities.Camera;

public sealed class CameraCapability : ICapability
{
    private readonly CameraCaptureWorker _worker;
    private readonly ILogger<CameraCapability> _logger;

    public CameraCapability(
        CameraCaptureWorker worker,
        ILogger<CameraCapability> logger)
    {
        _worker = worker;
        _logger = logger;
    }

    public CapabilityManifest Manifest =>
        new(
            Id: "camera.capture",
            Name: "Camera Capture",
            Version: "1.0.0");

    public async Task StartAsync(
        ICapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting capability {CapabilityId} for agent {AgentId}.",
            Manifest.Id,
            context.AgentId);

        await _worker.StartAsync(
            cancellationToken);

        _logger.LogInformation(
            "Capability {CapabilityId} started.",
            Manifest.Id);
    }

    public async Task StopAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Stopping capability {CapabilityId}.",
            Manifest.Id);

        await _worker.StopAsync(
            cancellationToken);

        _logger.LogInformation(
            "Capability {CapabilityId} stopped.",
            Manifest.Id);
    }
}