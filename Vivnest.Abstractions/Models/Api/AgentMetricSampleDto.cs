namespace Vivnest.Abstractions.Models.Api;

public sealed record AgentMetricSampleDto(
    DateTime OccurredAtUtc,
    double? CpuUsagePercent,
    long MemoryUsedBytes,
    long BytesUploaded);
