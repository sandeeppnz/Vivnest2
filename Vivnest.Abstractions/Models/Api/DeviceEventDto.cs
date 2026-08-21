using System.Text.Json;

namespace Vivnest.Abstractions.Models.Api;

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
    bool? SinkCleanlinessResult = null,
    // Same join, against the same capture's ObjectsDetected event - null
    // means ObjectDetection never ran on this capture, not "ran and found
    // nothing" (that's an empty array).
    IReadOnlyList<DetectedObjectDto>? DetectedObjects = null);

public sealed record DetectedObjectDto(
    string ClassName,
    float Confidence,
    int X1,
    int Y1,
    int X2,
    int Y2,
    bool Unusual);
