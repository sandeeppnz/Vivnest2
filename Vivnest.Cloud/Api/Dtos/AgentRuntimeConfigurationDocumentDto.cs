namespace Vivnest.Cloud.Api.Dtos;

public sealed record AgentRuntimeConfigurationDocumentDto(
    string? AgentId,
    string? Name,
    IReadOnlyList<AiDeviceClassificationEntryDto> Devices,
    IReadOnlyList<AgentCapabilityRuntimeDto> Capabilities,
    IReadOnlyList<string> Warnings,
    ConfigurationSyncStatusDto? SyncStatus = null);
