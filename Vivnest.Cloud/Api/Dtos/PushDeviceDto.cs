namespace Vivnest.Cloud.Api.Dtos;

// Deliberately has no ExpoPushToken: it is a credential for pushing to the
// phone, and nothing reading this list needs it.
public sealed record PushDeviceDto(
    string PushDeviceId,
    string Name,
    string Platform,
    bool Enabled,
    DateTime CreatedUtc,
    DateTime LastSeenUtc);

public sealed record RegisterPushDeviceRequest(
    string ExpoPushToken,
    string? Name,
    string? Platform);

public sealed record SendTestPushRequest(string? Channel);

public sealed record SendTestPushResult(int Phones, int Accepted);
