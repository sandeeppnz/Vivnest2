namespace Vivnest.Cloud.Interfaces;

public interface IDeviceEventRetentionService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
