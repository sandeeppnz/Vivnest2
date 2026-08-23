using Azure.Data.Tables;using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vivnest.Core.DataStores;
using Vivnest.Core.Options;
using Vivnest.Core.PhotoStores;
using Vivnest.Core.Queues;
using Vivnest.Core.Storage;
using Vivnest.Core.Utils;
using Vivnest.Infrastructure.DataStores;
using Vivnest.Infrastructure.Storage;
using Vivnest.Infrastructure.Utils;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    // Azure storage, writers and shared helpers. Device factories moved
    // to AddDeviceInfrastructure() in Vivnest.Infrastructure.Devices
    // (ADR-109) - registering them here would have required this project
    // to reference the device protocols, which is exactly what the split
    // removes from Cloud's dependency graph.
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {

        services.AddSingleton(sp =>
        {
            var options = sp
                .GetRequiredService<IOptions<StorageOptions>>()
                .Value;

            return new BlobServiceClient(options.ConnectionString);
        });

        services.AddSingleton(sp =>
        {
            var options = sp
                .GetRequiredService<IOptions<StorageOptions>>()
                .Value;

            return new TableServiceClient(options.ConnectionString);
        });

        services.AddSingleton<AzureBlobStorageClient>();

        // Same pairing Vivnest.Cloud already registers. Consumers that
        // only need the contract take IBlobStorageClient, which is what
        // lets a capability depend on Core rather than on Infrastructure.
        services.AddSingleton<IBlobStorageClient>(
            sp => sp.GetRequiredService<AzureBlobStorageClient>());
        services.AddSingleton<IPhotoStorage, AzureBlobStorage>();
        services.AddSingleton<IQueuePublisher, AzureQueuePublisher>();

        services.AddSingleton<IBlobNameGenerator, BlobNameGenerator>();
        services.AddSingleton<IDeviceRuntimeStore, DeviceRuntimeStore>();
        services.AddSingleton<IAgentHeartbeatWriter, AgentHeartbeatWriter>();
        services.AddSingleton<IDeviceHeartbeatWriter, DeviceHeartbeatWriter>();
        services.AddSingleton<IDeviceEventWriter, AzureTableDeviceEventWriter>();
        services.AddSingleton<IAgentEventWriter, AzureTableAgentEventWriter>();

        return services;
    }
}