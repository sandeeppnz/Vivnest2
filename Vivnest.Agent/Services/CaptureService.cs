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
    private readonly ICameraFactory _cameraFactory;
    private readonly IPhotoStorage _photoStorage;
    private readonly AgentOptions _agentOptions;
    private readonly StorageOptions _storageOptions;

    private readonly ILogger<CaptureService> _logger;
    private readonly IBlobNameGenerator _blobNameGenerator;

    public CaptureService(
        ICameraFactory cameraFactory,
        IPhotoStorage photoStorage,
        IBlobNameGenerator blobNameGenerator,
        IOptions<AgentOptions> agentOptions,
        IOptions<StorageOptions> storageOptions,
        ILogger<CaptureService> logger)
    {
        _cameraFactory = cameraFactory;
        _photoStorage = photoStorage;
        _agentOptions = agentOptions.Value;
        _storageOptions = storageOptions.Value;
        _blobNameGenerator = blobNameGenerator;
        _logger = logger;
    }

    public async Task<CaptureResult> CaptureAsync(
        DeviceOptions cameraOptions,
        CancellationToken cancellationToken = default)
    {
        var capturedAtUtc = DateTime.UtcNow;

        try
        {
            _logger.LogInformation(
                "Starting image capture for device {DeviceId}...",
                cameraOptions.DeviceId);

            //
            // Capture image
            //
            var camera = _cameraFactory.Create(cameraOptions);

            var captureWatch = Stopwatch.StartNew();

            await using var image =
                await camera.CaptureAsync(cancellationToken);

            captureWatch.Stop();

            //
            // Generate blob name
            //
            var blobName = _blobNameGenerator.Generate(
                new BlobNameContext(
                    TenantId: _agentOptions.TenantId,
                    SiteId: _agentOptions.SiteId,
                    AgentId: _agentOptions.AgentId,
                    CameraId: cameraOptions.DeviceId,
                    CapturedAt: capturedAtUtc,
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
                cameraOptions.DeviceId,
                blobName);

            return new CaptureResult
            {
                Success = true,
                DeviceId = cameraOptions.DeviceId,
                CapturedAtUtc = capturedAtUtc,
                BlobName = blobName,
                BlobContainer = _storageOptions.BlobContainer,
                CaptureDuration = captureWatch.Elapsed,
                UploadDuration = uploadWatch.Elapsed,
                CaptureInterval = cameraOptions.ActivityInterval
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Capture failed for device {DeviceId}",
                cameraOptions.DeviceId);

            return new CaptureResult
            {
                Success = false,
                DeviceId = cameraOptions.DeviceId,
                CapturedAtUtc = capturedAtUtc,
                Error = ex.Message
            };
        }
    }
}
