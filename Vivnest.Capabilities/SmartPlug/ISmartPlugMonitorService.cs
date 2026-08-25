using Vivnest.Core.Options;
using Vivnest.Core.Devices.SmartPlug.Models;

namespace Vivnest.Capabilities.SmartPlug;

public interface ISmartPlugMonitorService
{
    Task<SmartPlugReadingResult> ReadAsync(
        DeviceOptions plugOptions,
        CancellationToken cancellationToken);

    Task<bool> CheckReachabilityAsync(
        DeviceOptions plugOptions,
        CancellationToken cancellationToken);
}
