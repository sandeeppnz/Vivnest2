namespace Vivnest.Agent.Runtime.Events;

public sealed record AgentMetricsSampledEvent(
    DateTime SampledAtUtc,
    double? CpuUsagePercent,
    long MemoryUsedBytes);
