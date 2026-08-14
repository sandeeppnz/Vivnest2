namespace Vivnest.Cloud.Api.Dtos;

// Read-only preview of what the Admin domain WOULD produce for this
// Device's runtime device-config/*.json shape (decision-log.md ADR-063) -
// nothing writes anywhere, this is purely for a human to eyeball-diff
// against the real file. Deliberately scoped to identity + connection
// Settings only - LivenessInterval/Schedule/Trigger/SinkCleanliness/
// ObjectDetection/Sensors are NOT projected yet (see ADR-063 for why).
public sealed record ProjectedDeviceConfigDto(
    string? DeviceId,
    string Name,
    string? Type,
    bool Enabled,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    string? OwningAgentId,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string> Warnings);
