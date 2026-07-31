namespace Vivnest.Cloud.Interfaces;

public interface IHealthMonitorService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
