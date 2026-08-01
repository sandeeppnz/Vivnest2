using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Api;

public interface IDeviceQueryService
{
    Task<IReadOnlyList<DeviceSummaryDto>> GetDevicesAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default);

    Task<DeviceSummaryDto?> GetDeviceAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceEventDto>> GetDeviceEventsAsync(
        TenantContext tenant,
        string deviceId,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceEventDto>> GetDeviceCapturesAsync(
        TenantContext tenant,
        string deviceId,
        int take,
        CancellationToken cancellationToken = default);
}
