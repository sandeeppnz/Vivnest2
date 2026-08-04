namespace Vivnest.Agent.Runtime.Shell;

public sealed record AgentMetricsSampledEvent(
    DateTime SampledAtUtc,
    double? CpuUsagePercent,
    long MemoryUsedBytes,
    long BytesUploaded);
