using Microsoft.Extensions.Logging;
using Vivnest.Core.Options;
using Vivnest.Core.SmartPlug;
using Vivnest.Core.SmartPlug.Models;

namespace Vivnest.Agent.Capabilities.SmartPlug;

public class SmartPlugMonitorService : ISmartPlugMonitorService
{
    private readonly ISmartPlugFactory _plugFactory;
    private readonly ILogger<SmartPlugMonitorService> _logger;

    public SmartPlugMonitorService(
        ISmartPlugFactory plugFactory,
        ILogger<SmartPlugMonitorService> logger)
    {
        _plugFactory = plugFactory;
        _logger = logger;
    }

    public async Task<SmartPlugReadingResult> ReadAsync(
        DeviceOptions plugOptions,
        CancellationToken cancellationToken = default)
    {
        var readAtUtc = DateTime.UtcNow;

        try
        {
            _logger.LogInformation(
                "Reading state for smart plug {DeviceId}...",
                plugOptions.DeviceId);

            var plug = _plugFactory.Create(plugOptions);

            var state = await plug.GetStateAsync(cancellationToken);

            return new SmartPlugReadingResult
            {
                Success = true,
                DeviceId = plugOptions.DeviceId,
                ReadAtUtc = readAtUtc,
                State = state,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Reading failed for smart plug {DeviceId}",
                plugOptions.DeviceId);

            return new SmartPlugReadingResult
            {
                Success = false,
                DeviceId = plugOptions.DeviceId,
                ReadAtUtc = readAtUtc,
                Error = ex.Message,
            };
        }
    }

    public async Task<bool> CheckReachabilityAsync(
        DeviceOptions plugOptions,
        CancellationToken cancellationToken)
    {
        var plug = _plugFactory.Create(plugOptions);

        return await plug.IsReachableAsync(cancellationToken);
    }
}
