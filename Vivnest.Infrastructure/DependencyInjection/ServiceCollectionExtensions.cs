using Microsoft.Extensions.DependencyInjection;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models.Camera;
using Vivnest.Infrastructure.Camera;
using Vivnest.Infrastructure.Services;
using Vivnest.Infrastructure.Storage;

namespace Vivnest.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<CaptureStatusStore>();

        services.AddSingleton<ICameraFactory, CameraFactory>();

        services.AddSingleton<IPhotoStorage, AzureBlobStorage>();
        services.AddSingleton<IQueuePublisher, AzureQueuePublisher>();


        return services;
    }
}