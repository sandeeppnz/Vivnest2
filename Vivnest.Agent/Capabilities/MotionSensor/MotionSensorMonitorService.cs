using Microsoft.Extensions.Logging;
using Vivnest.Core.MotionSensor;
using Vivnest.Core.MotionSensor.Models;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.MotionSensor;

public class MotionSensorMonitorService : IMotionSensorMonitorService
{
    private readonly IMotionSensorFactory _sensorFactory;
    private readonly ILogger<MotionSensorMonitorService> _logger;

    public MotionSensorMonitorService(
        IMotionSensorFactory sensorFactory,
        ILogger<MotionSensorMonitorService> logger)
    {
        _sensorFactory = sensorFactory;
        _logger = logger;
    }

    public async Task<MotionSensorReadingResult> ReadAsync(
        DeviceOptions sensorOptions,
        CancellationToken cancellationToken = default)
    {
        var readAtUtc = DateTime.UtcNow;

        try
        {
            _logger.LogInformation(
                "Reading state for motion sensor {DeviceId}...",
                sensorOptions.DeviceId);

            var sensor = _sensorFactory.Create(sensorOptions);

            var state = await sensor.GetStateAsync(cancellationToken);

            return new MotionSensorReadingResult
            {
                Success = true,
                DeviceId = sensorOptions.DeviceId,
                ReadAtUtc = readAtUtc,
                State = state,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Reading failed for motion sensor {DeviceId}",
                sensorOptions.DeviceId);

            return new MotionSensorReadingResult
            {
                Success = false,
                DeviceId = sensorOptions.DeviceId,
                ReadAtUtc = readAtUtc,
                Error = ex.Message,
            };
        }
    }

    public async Task<bool> CheckReachabilityAsync(
        DeviceOptions sensorOptions,
        CancellationToken cancellationToken)
    {
        var sensor = _sensorFactory.Create(sensorOptions);

        return await sensor.IsReachableAsync(cancellationToken);
    }
}
