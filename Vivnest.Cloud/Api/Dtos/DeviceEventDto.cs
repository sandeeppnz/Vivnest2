using System.Text.Json;

namespace Vivnest.Cloud.Api.Dtos;

public sealed record DeviceEventDto(
    string EventType,
    string Severity,
    DateTime OccurredAtUtc,
    JsonElement? Data);
