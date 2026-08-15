namespace Vivnest.Cloud.Interfaces;

public interface ICommandExpiryService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
