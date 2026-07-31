using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vivnest.Core.Camera;
using Vivnest.Core.DataStores;
using Vivnest.Core.Options;
using Vivnest.Core.PhotoStores;
using Vivnest.Core.Queues;
using Vivnest.Core.Storage;
using Vivnest.Core.Utils;
using Vivnest.Infrastructure.Camera;
using Vivnest.Infrastructure.DataStores;
using Vivnest.Infrastructure.Storage;
using Vivnest.Infrastructure.Utils;

namespace Vivnest.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<ICameraFactory, CameraFactory>();

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
        services.AddSingleton<IPhotoStorage, AzureBlobStorage>();
        services.AddSingleton<IQueuePublisher, AzureQueuePublisher>();

        services.AddSingleton<IBlobNameGenerator, BlobNameGenerator>();
        services.AddSingleton<IDeviceRuntimeStore, DeviceRegistry>();
        services.AddSingleton<IAgentHeartbeatWriter, AgentHeartbeatWriter>();
        services.AddSingleton<IDeviceHeartbeatWriter, DeviceHeartbeatWriter>();
        services.AddSingleton<IDeviceEventWriter, AzureTableDeviceEventWriter>();

        return services;
    }
}