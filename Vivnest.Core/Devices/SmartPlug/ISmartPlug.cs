using Vivnest.Core.Devices.SmartPlug.Models;

namespace Vivnest.Core.Devices.SmartPlug;

public interface ISmartPlug
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);

    Task<SmartPlugState> GetStateAsync(CancellationToken cancellationToken = default);
}
