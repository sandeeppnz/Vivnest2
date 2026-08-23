using Azure.Storage.Queues;
using System.Text.Json;
using Vivnest.Core.Queues;
using Vivnest.Core.Storage;

namespace Vivnest.Infrastructure.Azure;

// Shared by both Agent (Infrastructure's DI) and Cloud (Cloud's DI) - moved
// here from Vivnest.Infrastructure once Cloud needed to publish too
// (RestartCommandQueueMessage, the first Cloud-to-Agent message). Nothing
// about this class is Agent-specific, same reasoning AzureBlobStorageClient
// already lives here rather than in Infrastructure.
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
