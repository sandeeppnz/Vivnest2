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

    // Motion sensor battery/signal history - mirrors GetDeviceCapturesAsync's
    // shape (filter by EventType, newest-first), just for BatteryStatus
    // readings instead of CameraCaptured.
    Task<IReadOnlyList<DeviceEventDto>> GetDeviceBatteryReadingsAsync(
        TenantContext tenant,
        string deviceId,
        int take,
        CancellationToken cancellationToken = default);

    // Per-day counts only, no SAS URLs generated - cheap enough to load the
    // whole window up front so the gallery can render every collapsed day
    // header immediately, without paying for images nobody's looking at yet.
    Task<IReadOnlyList<CaptureDaySummaryDto>> GetDeviceCaptureDaySummariesAsync(
        TenantContext tenant,
        string deviceId,
        int days,
        CancellationToken cancellationToken = default);

    // One day, one page at a time (newest first) - called when a day is
    // actually expanded, and again for each "load more" within it.
    Task<CapturePageDto> GetDeviceCapturesByDayAsync(
        TenantContext tenant,
        string deviceId,
        DateOnly date,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
