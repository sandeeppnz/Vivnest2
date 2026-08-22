using Microsoft.Extensions.DependencyInjection;
using Vivnest.Abstraction.Agent.Capabilities;
using Vivnest.Abstraction.Agent.Events;
using Vivnest.Agent.Capabilities.Bridges.HomeAssistant;
using Vivnest.Agent.Capabilities.Bridges.TapoHub;
using Vivnest.Agent.Capabilities.Camera;
using Vivnest.Agent.Capabilities.MotionSensor;
using Vivnest.Agent.Capabilities.SmartPlug;
using Vivnest.Agent.Capabilities.Triggers;
using Vivnest.Core.Enums;

namespace Vivnest.Agent.Bootstrap;

public static class AgentCapabilityRegistration
{
    public static IServiceCollection AddAgentCapabilities(
        this IServiceCollection services,
        AgentType agentType)
    {
        switch (agentType)
        {
            case AgentType.Low:
                AddLowAgentCapabilities(services);
                break;

            case AgentType.High:
                AddHighAgentCapabilities(services);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported AgentType '{agentType}'.");
        }

        return services;
    }

    // =====================================================================
    // LOW AGENT
    // =====================================================================

    private static void AddLowAgentCapabilities(
        IServiceCollection services)
    {
        // -----------------------------------------------------------------
        // CAMERA
        // -----------------------------------------------------------------

        services.AddSingleton<CameraCapability>();

        services.AddSingleton<ICapability>(
            sp => sp.GetRequiredService<CameraCapability>());

        services.AddSingleton<
            CameraCaptureWorker>();

        services.AddSingleton<
            IEventHandler<CameraCaptureCompletedEvent>,
            CameraCaptureHandler>();

        services.AddSingleton<
            IEventHandler<CameraCaptureCompletedEvent>,
            SinkCleanlinessHandler>();

        services.AddSingleton<
            IEventHandler<CameraCaptureFailedEvent>,
            CameraCaptureFailedHandler>();

        services.AddSingleton<
            ICameraCaptureService,
            CameraCaptureService>();

        services.AddSingleton<
            ICameraCaptureExecutor,
            CameraCaptureExecutor>();


        // -----------------------------------------------------------------
        // SMART PLUG
        // -----------------------------------------------------------------

        services.AddSingleton<
            SmartPlugCapability>();

        services.AddSingleton<ICapability>(
            sp => sp.GetRequiredService<SmartPlugCapability>());

        services.AddSingleton<
            IEventHandler<SmartPlugReadingCompletedEvent>,
            SmartPlugReadingHandler>();

        services.AddSingleton<
            SmartPlugMonitorWorker>();

        services.AddSingleton<
            IEventHandler<SmartPlugReadingFailedEvent>,
            SmartPlugReadingFailedHandler>();

        services.AddSingleton<
            IEventHandler<SmartPlugPowerStateChangedEvent>,
            SmartPlugPowerStateChangedHandler>();

        services.AddSingleton<
            ISmartPlugMonitorService,
            SmartPlugMonitorService>();


        // -----------------------------------------------------------------
        // MOTION SENSOR
        // -----------------------------------------------------------------

        services.AddSingleton<
            MotionSensorCapability>();

        services.AddSingleton<ICapability>(
            sp => sp.GetRequiredService<MotionSensorCapability>());

        services.AddSingleton<
            IEventHandler<MotionSensorStateChangedEvent>,
            MotionSensorStateChangedHandler>();

        services.AddSingleton<
            MotionSensorMonitorWorker>();

        services.AddSingleton<
            IEventHandler<MotionSensorStateChangedEvent>,
            MotionTriggerResolverHandler>();

        services.AddSingleton<
            IEventHandler<MotionSensorReadingFailedEvent>,
            MotionSensorReadingFailedHandler>();

        services.AddSingleton<
            IEventHandler<MotionSensorBatteryReportedEvent>,
            MotionSensorBatteryHandler>();

        services.AddSingleton<
            IMotionSensorMonitorService,
            MotionSensorMonitorService>();


        // -----------------------------------------------------------------
        // HOME ASSISTANT
        // -----------------------------------------------------------------

        services.AddSingleton<
            IEventHandler<HomeAssistantStateChangedEvent>,
            HomeAssistantStateChangedHandler>();

        services.AddHttpClient<
            IHomeAssistantCommandSender,
            HomeAssistantCommandSender>();

        services.AddSingleton<
            IHomeAssistantLivenessTracker,
            HomeAssistantLivenessTracker>();

        services.AddHostedService<
            HomeAssistantWorker>();


        // -----------------------------------------------------------------
        // TAPO HUB
        // -----------------------------------------------------------------

        services.AddSingleton<
            ITapoHubReachabilityChecker,
            TapoHubReachabilityChecker>();

        services.AddHostedService<
            TapoHubLivenessWorker>();


        // -----------------------------------------------------------------
        // TRIGGERS
        // -----------------------------------------------------------------

        services.AddSingleton<
            IEventHandler<DeviceTriggeredEvent>,
            CaptureOnTriggerHandler>();
    }


    // =====================================================================
    // HIGH AGENT
    // =====================================================================

    private static void AddHighAgentCapabilities(
        IServiceCollection services)
    {
        // -----------------------------------------------------------------
        // SINK CLEANLINESS
        // -----------------------------------------------------------------

        services.AddSingleton<
            ISinkCleanlinessClassifier,
            SinkCleanlinessClassifier>();


        // -----------------------------------------------------------------
        // OBJECT DETECTION
        // -----------------------------------------------------------------

        services.AddSingleton<
            IObjectDetector,
            ObjectDetector>();


        // -----------------------------------------------------------------
        // SINK CLEANLINESS WORKER
        // -----------------------------------------------------------------

        services.AddHostedService<
            SinkCleanlinessWorker>();
    }
}
