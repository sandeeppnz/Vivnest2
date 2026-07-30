namespace Vivnest.Core.Queues;

public interface IQueuePublisher
{
    Task PublishAsync<T>(
        string queueName,
        T message,
        CancellationToken cancellationToken = default);
}