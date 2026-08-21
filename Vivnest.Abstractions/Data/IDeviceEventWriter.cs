using Vivnest.Abstractions.Domain;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Abstractions.Data;

public interface IDeviceEventWriter
{
    Task<DeviceEventEntity?> SaveAsync(
        DeviceEvent deviceEvent,
        CancellationToken cancellationToken = default);
}
