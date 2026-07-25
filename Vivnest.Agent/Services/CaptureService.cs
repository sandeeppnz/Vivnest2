using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models;
using Vivnest.Core.Models.Camera;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Services;

public class CaptureService : ICaptureService
{
    private readonly ICamera _camera;
    private readonly IPhotoStorage _photoStorage;
    private readonly AgentOptions _agentOptions;
    private readonly DeviceOptions _cameraOptions;
    private readonly ILogger<CaptureService> _logger;
    private readonly IBlobNameGenerator _blobNameGenerator;
    
    
    public CaptureService(
        ICamera camera,
        IPhotoStorage photoStorage,
        IDeviceRegistry deviceRegistry,
        IBlobNameGenerator blobNameGenerator,
        IOptions<AgentOptions> agentOptions,
        ILogger<CaptureService> logger)
    {
        _camera = camera;
        _cameraOptions = deviceRegistry.GetCamera();
        _photoStorage = photoStorage;
        _agentOptions = agentOptions.Value;
        _blobNameGenerator = blobNameGenerator;
        _logger = logger;
    }

    public async Task<CaptureResult> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var capturedAt = DateTime.UtcNow;

        try
        {
            _logger.LogInformation(
                "Starting image capture for device {DeviceId}...",
                _cameraOptions.DeviceId);

            //
            // Capture image
            //
            var captureWatch = Stopwatch.StartNew();

            await using var image =
                await _camera.CaptureAsync(cancellationToken);

            captureWatch.Stop();

            //
            // Generate blob name
            //
            var blobName = _blobNameGenerator.Generate(
                new BlobNameContext(
                    AgentId: _agentOptions.AgentId,
                    CameraId: _cameraOptions.DeviceId,
                    CapturedAt: capturedAt,
                    Extension: ".jpg"));

            //
            // Upload image
            //
            var uploadWatch = Stopwatch.StartNew();

            await _photoStorage.UploadAsync(
                image,
                blobName,
                cancellationToken);

            uploadWatch.Stop();

            _logger.LogInformation(
                "Image uploaded for device {DeviceId} to {BlobName}",
                _cameraOptions.DeviceId,
                blobName);

            return new CaptureResult
            {
                Success = true,
                DeviceId = _cameraOptions.DeviceId,
                CapturedAt = capturedAt,
                BlobName = blobName,
                CaptureDuration = captureWatch.Elapsed,
                UploadDuration = uploadWatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Capture failed for device {DeviceId}",
                _cameraOptions.DeviceId);

            return new CaptureResult
            {
                Success = false,
                DeviceId = _cameraOptions.DeviceId,
                CapturedAt = capturedAt,
                Error = ex.Message
            };
        }
    }
}