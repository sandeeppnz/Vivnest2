using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Camera;
using Vivnest.Core.Heartbeat;
using Vivnest.Core.Models;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Agent.Services;

public class CaptureService : ICaptureService
{
    private readonly ICamera _camera;
    private readonly IPhotoStorage _photoStorage;
    private readonly IHeartbeatService _heartbeatService;
    private readonly AgentOptions _agentOptions;
    private readonly CameraOptions _cameraOptions;
    private readonly ILogger<CaptureService> _logger;
    private readonly IBlobNameGenerator _blobNameGenerator;
    public CaptureService(
        ICamera camera,
        IPhotoStorage photoStorage,
        IHeartbeatService heartbeatService,
        IBlobNameGenerator blobNameGenerator,
        IOptions<AgentOptions> agentOptions,
        IOptions<CameraOptions> cameraOptions,
        ILogger<CaptureService> logger)
    {
        _camera = camera;
        _photoStorage = photoStorage;
        _heartbeatService = heartbeatService;
        _agentOptions = agentOptions.Value;
        _cameraOptions = cameraOptions.Value;
        _blobNameGenerator = blobNameGenerator;
        _logger = logger;
    }

    public async Task<CaptureResult> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var capturedAt = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Starting image capture...");

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
                    CameraId: _cameraOptions.CameraId,
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
                "Image uploaded to {BlobName}",
                blobName);

            //
            // Send heartbeat
            //
            return new CaptureResult
            {
                Success = true,
                CapturedAt = capturedAt,
                BlobName = blobName,
                CaptureDuration = captureWatch.Elapsed,
                UploadDuration = uploadWatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Capture failed.");

            return new CaptureResult
            {
                Success = false,
                CapturedAt = capturedAt,
                Error = ex.Message
            };
        }
    }
}