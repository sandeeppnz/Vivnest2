using Microsoft.Extensions.DependencyInjection;
using Vivnest.Core.Devices.Camera;
using Vivnest.Core.Devices.MotionSensor;
using Vivnest.Core.Devices.SmartPlug;
using Vivnest.Infrastructure.Camera;
using Vivnest.Infrastructure.MotionSensor;

namespace Vivnest.Infrastructure.DependencyInjection;

// The device half of what AddInfrastructure() used to register (ADR-109).
// Separate so that Vivnest.Cloud, which needs Azure storage and nothing
// else from Infrastructure, does not compile against device protocols.
public static class DeviceServiceCollectionExtensions
{
    public static IServiceCollection AddDeviceInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<ICameraFactory, CameraFactory>();
        services.AddSingleton<ISmartPlugFactory, SmartPlug.SmartPlugFactory>();
        services.AddSingleton<IMotionSensorFactory, MotionSensorFactory>();

        return services;
    }
}
