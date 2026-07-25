using Azure.Storage.Queues;
using System.Text.Json;
using Vivnest.Core.Interfaces;

namespace Vivnest.Infrastructure.Storage;

public sealed class AzureQueuePublisher : IQueuePublisher
{
    private readonly QueueServiceClient _queueServiceClient;

    public AzureQueuePublisher(
        QueueServiceClient queueServiceClient)
    {
        _queueServiceClient = queueServiceClient;
    }

    public async Task PublishAsync<T>(
        string queueName,
        T message,
        CancellationToken cancellationToken = default)
    {
        var queue = _queueServiceClient.GetQueueClient(queueName);

        await queue.CreateIfNotExistsAsync(
            cancellationToken: cancellationToken);

        var json = JsonSerializer.Serialize(message);

        await queue.SendMessageAsync(
            json,
            cancellationToken);
    }
}