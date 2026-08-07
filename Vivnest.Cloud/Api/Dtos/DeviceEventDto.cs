using System.Text.Json;

namespace Vivnest.Cloud.Api.Dtos;

public sealed record DeviceEventDto(
    string DeviceId,
    string DeviceType,
    string EventType,
    string Severity,
    DateTime OccurredAtUtc,
    JsonElement? Data,
    string? ImageUrl,
    // Only populated for CameraCaptured rows returned by
    // GetDeviceCapturesByDayAsync, joined server-side against any
    // SinkCleanliness event with the same OccurredAtUtc (both trace back
    // to the same CameraCaptureResult.CapturedAtUtc - see ADR-034's
    // follow-up). Null for every other event type, and for a capture that
    // was never classified.
    bool? SinkCleanlinessResult = null);
