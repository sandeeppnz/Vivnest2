namespace Vivnest.Cloud.Api.Dtos;

public interface IMonitorable
{
    string Id { get; }
    string Status { get; }
    DateTime? StatusSinceUtc { get; }
    DateTime LastHeartbeatUtc { get; }
}
