using Microsoft.Extensions.DependencyInjection;

namespace Vivnest.Infrastructure.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddVivnestInfrastructure(this IServiceCollection services)
        {
            // Register infrastructure services here, e.g.:
            // services.AddSingleton<ICameraService, CameraService>();
            // services.AddSingleton<IStorageService, StorageService>();
            return services;
        }
    }
}
