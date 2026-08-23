using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Devices;

namespace Vivnest.Core.DataStores;

public interface IDeviceEventWriter
{
    Task<DeviceEventEntity?> SaveAsync(
        DeviceEvent deviceEvent,
        CancellationToken cancellationToken = default);
}
