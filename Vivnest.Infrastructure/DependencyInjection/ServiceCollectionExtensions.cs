using Microsoft.Extensions.DependencyInjection;
using Vivnest.Core.Camera;
using Vivnest.Core.Models;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Camera;
using Vivnest.Infrastructure.Storage;

namespace Vivnest.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<RtspCamera>();
        services.AddSingleton<CaptureStatus>();

        services.AddSingleton<ICamera, TapoC120Camera>();

        services.AddSingleton<IPhotoStorage, AzureBlobStorage>();

        return services;
    }
}