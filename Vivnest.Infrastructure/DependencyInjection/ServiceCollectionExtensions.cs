using Microsoft.Extensions.DependencyInjection;
using Vivnest.Core.Camera;
using Vivnest.Core.PhotoStores;
using Vivnest.Core.Queues;
using Vivnest.Infrastructure.Camera;
using Vivnest.Infrastructure.Storage;

namespace Vivnest.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<ICameraFactory, CameraFactory>();

        services.AddSingleton<IPhotoStorage, AzureBlobStorage>();
        services.AddSingleton<IQueuePublisher, AzureQueuePublisher>();


        return services;
    }
}