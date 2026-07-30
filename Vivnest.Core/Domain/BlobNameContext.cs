namespace Vivnest.Core.Domain;

public record BlobNameContext(
    string TenantId,
    string SiteId,
    string AgentId,
    string CameraId,
    DateTime CapturedAt,
    string Extension);
