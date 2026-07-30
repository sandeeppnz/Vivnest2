using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;

namespace Vivnest.Core.DataStores;

public interface IDeviceEventStore
{
    Task<DeviceEventEntity?> SaveAsync(
        DeviceEvent deviceEvent,
        CancellationToken cancellationToken = default);
}