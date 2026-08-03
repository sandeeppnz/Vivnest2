namespace Vivnest.Cloud.Api.Dtos;

public sealed record AgentMetricSampleDto(
    DateTime OccurredAtUtc,
    double? CpuUsagePercent,
    long MemoryUsedBytes,
    long BytesUploaded);
