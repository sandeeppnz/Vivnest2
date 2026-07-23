using Microsoft.Extensions.DependencyInjection;
using Vivnest.Core.Camera;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Camera;
using Vivnest.Infrastructure.Storage;

namespace Vivnest.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<ICamera, TapoCamera>();

        services.AddSingleton<IPhotoStorage, AzureBlobStorage>();

        return services;
    }
}