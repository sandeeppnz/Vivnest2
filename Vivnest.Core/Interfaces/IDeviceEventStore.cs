using Vivnest.Core.Models;

namespace Vivnest.Core.Interfaces;

public interface IDeviceEventStore
{
    Task SaveAsync(
        DeviceEvent deviceEvent,
        CancellationToken cancellationToken = default);
}