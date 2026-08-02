using Vivnest.Core.SmartPlug.Models;

namespace Vivnest.Core.SmartPlug;

public interface ISmartPlug
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);

    Task<SmartPlugState> GetStateAsync(CancellationToken cancellationToken = default);
}
