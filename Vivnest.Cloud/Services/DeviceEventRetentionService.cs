using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Options;

namespace Vivnest.Cloud.Services;

public sealed class DeviceEventRetentionService : IDeviceEventRetentionService
{
    private readonly IDeviceEventReader _deviceEvents;
    private readonly DeviceEventRetentionOptions _options;
    private readonly ILogger<DeviceEventRetentionService> _logger;

    public DeviceEventRetentionService(
        IDeviceEventReader deviceEvents,
        IOptions<DeviceEventRetentionOptions> options,
        ILogger<DeviceEventRetentionService> logger)
    {
        _deviceEvents = deviceEvents;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Device event retention disabled.");
            return;
        }

        var cutoffUtc = DateTime.UtcNow.AddDays(-_options.RetentionDays);

        _logger.LogInformation(
            "Deleting DeviceEvent rows older than {CutoffUtc:u} (retention: {RetentionDays} days).",
            cutoffUtc,
            _options.RetentionDays);

        var deleted = await _deviceEvents.DeleteOlderThanAsync(cutoffUtc, cancellationToken);

        _logger.LogInformation("Deleted {Count} DeviceEvent row(s).", deleted);
    }
}
