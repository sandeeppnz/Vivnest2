using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Functions;

public class DeviceHeartbeatChangedFunction
{
    private readonly ILogger<DeviceHeartbeatChangedFunction> _logger;
    private readonly IHealthMonitorService _healthMonitorService;

    public DeviceHeartbeatChangedFunction(
        ILogger<DeviceHeartbeatChangedFunction> logger,
        IHealthMonitorService healthMonitorService)
    {
        _logger = logger;
        _healthMonitorService = healthMonitorService;
    }

    [Function(nameof(DeviceHeartbeatChangedFunction))]
    public async Task Run(
        [QueueTrigger("device-heartbeats")]
            DeviceHeartbeatQueueMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await _healthMonitorService.ProcessDeviceAsync(
                message.PartitionKey,
                message.RowKey,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed processing device heartbeat change {PartitionKey}/{RowKey}",
                message.PartitionKey,
                message.RowKey);

            throw; // Important: rethrow so Azure Functions retries
        }
    }
}
