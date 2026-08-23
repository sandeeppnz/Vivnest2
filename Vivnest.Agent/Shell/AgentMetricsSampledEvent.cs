namespace Vivnest.Agent.Shell;

public sealed record AgentMetricsSampledEvent(
    DateTime SampledAtUtc,
    double? CpuUsagePercent,
    long MemoryUsedBytes,
    long BytesUploaded);
