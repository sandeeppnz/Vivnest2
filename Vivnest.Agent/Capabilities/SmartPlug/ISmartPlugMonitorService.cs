using Vivnest.Core.Options;
using Vivnest.Core.SmartPlug.Models;

namespace Vivnest.Agent.Capabilities.SmartPlug;

public interface ISmartPlugMonitorService
{
    Task<SmartPlugReadingResult> ReadAsync(
        DeviceOptions plugOptions,
        CancellationToken cancellationToken);

    Task<bool> CheckReachabilityAsync(
        DeviceOptions plugOptions,
        CancellationToken cancellationToken);
}
