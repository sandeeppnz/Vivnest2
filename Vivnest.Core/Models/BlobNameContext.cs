namespace Vivnest.Core.Models;

public record BlobNameContext(
    string TenantId,
    string SiteId,
    string AgentId,
    string CameraId,
    DateTime CapturedAt,
    string Extension);
