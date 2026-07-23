namespace Vivnest.Core.Models;

public record BlobNameContext(
    string AgentId,
    string CameraId,
    DateTime CapturedAt,
    string Extension);
