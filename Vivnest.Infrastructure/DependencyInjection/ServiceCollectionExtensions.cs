using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vivnest.Core.Camera;
using Vivnest.Core.Options;
using Vivnest.Core.PhotoStores;
using Vivnest.Core.Queues;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Camera;
using Vivnest.Infrastructure.Storage;

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

        services.AddSingleton<AzureBlobStorageClient>();
        services.AddSingleton<IPhotoStorage, AzureBlobStorage>();
        services.AddSingleton<IQueuePublisher, AzureQueuePublisher>();


        return services;
    }
}