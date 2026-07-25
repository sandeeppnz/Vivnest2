using Vivnest.Core.Entities;
using Vivnest.Core.Models;

namespace Vivnest.Core.Interfaces;

public interface IDeviceEventStore
{
    Task<DeviceEventEntity?> SaveAsync(
        DeviceEvent deviceEvent,
        CancellationToken cancellationToken = default);
}