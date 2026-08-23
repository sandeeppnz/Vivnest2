using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Vivnest.Core.Runtime;
using Vivnest.Core.Camera;
using Vivnest.Core.Camera.Models;
using Vivnest.Core.Options;
using Vivnest.Core.PhotoStores;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Capabilities.Camera;

public class CameraCaptureService : ICameraCaptureService
{
    private readonly ICameraFactory _cameraFactory;
    private readonly IPhotoStorage _photoStorage;
    private readonly INetworkUsageTracker _networkUsageTracker;
    private readonly AgentOptions _agentOptions;
    private readonly StorageOptions _storageOptions;

    private readonly ILogger<CameraCaptureService> _logger;
    private readonly IBlobNameGenerator _blobNameGenerator;

    public CameraCaptureService(
        ICameraFactory cameraFactory,
        IPhotoStorage photoStorage,
        INetworkUsageTracker networkUsageTracker,
        IBlobNameGenerator blobNameGenerator,
        IOptions<AgentOptions> agentOptions,
        IOptions<StorageOptions> storageOptions,
        ILogger<CameraCaptureService> logger)
    {
        _cameraFactory = cameraFactory;
        _photoStorage = photoStorage;
        _networkUsageTracker = networkUsageTracker;
        _agentOptions = agentOptions.Value;
        _storageOptions = storageOptions.Value;
        _blobNameGenerator = blobNameGenerator;
        _logger = logger;
    }

    public async Task<CameraCaptureResult> CaptureAsync(
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

            // CanSeek-guarded since ICamera only promises a Stream - RtspCamera's
            // is a MemoryStream today, but Length isn't universally supported.
            if (image.CanSeek)
                _networkUsageTracker.AddBytesUploaded(image.Length);

            _logger.LogInformation(
                "Image uploaded for device {DeviceId} to {BlobName}",
                cameraOptions.DeviceId,
                blobName);

            return new CameraCaptureResult
            {
                Success = true,
                DeviceId = cameraOptions.DeviceId,
                CapturedAtUtc = capturedAtUtc,
                BlobName = blobName,
                BlobContainer = _storageOptions.BlobContainer,
                CaptureDuration = captureWatch.Elapsed,
                UploadDuration = uploadWatch.Elapsed,
                CaptureInterval = cameraOptions.LivenessInterval
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller (e.g. graceful shutdown/restart) cancelled us - not a capture
            // failure, so don't log or report it as one.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Capture failed for device {DeviceId}",
                cameraOptions.DeviceId);

            return new CameraCaptureResult
            {
                Success = false,
                DeviceId = cameraOptions.DeviceId,
                CapturedAtUtc = capturedAtUtc,
                Error = ex.Message
            };
        }
    }

    public async Task<bool> CheckReachabilityAsync(
        DeviceOptions cameraOptions,
        CancellationToken cancellationToken)
    {
        var camera = _cameraFactory.Create(cameraOptions);

        return await camera.IsReachableAsync(cancellationToken);
    }
}
