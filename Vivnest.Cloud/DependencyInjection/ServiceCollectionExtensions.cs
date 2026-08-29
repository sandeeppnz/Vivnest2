using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Admin.Seeding;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Handlers;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Cloud.Queues;
using Vivnest.Cloud.Repositories;
using Vivnest.Cloud.Rules;
using Vivnest.Cloud.Services;
using Vivnest.Cloud.Storage;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCloud(
        this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var options = sp
                .GetRequiredService<IOptions<StorageOptions>>()
                .Value;

            return new TableServiceClient(options.ConnectionString);
        });

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

            return new QueueServiceClient(options.ConnectionString);
        });

        services.AddSingleton<AzureBlobStorageClient>();
        services.AddSingleton<IBlobStorageClient>(sp => sp.GetRequiredService<AzureBlobStorageClient>());
        services.AddSingleton<IAgentConfigurationStore, AzureTableAgentConfigurationStore>();
        services.AddSingleton<IConfigurationStateStore<AgentConfigurationEntity>>(
            sp => sp.GetRequiredService<IAgentConfigurationStore>());
        services.AddSingleton<IAgentEventStore, AzureTableAgentEventStore>();
        services.AddSingleton<IDeviceEventStore, AzureTableDeviceEventStore>();
        services.AddSingleton<IDeviceConfigurationStore, AzureTableDeviceConfigurationStore>();
        services.AddSingleton<IConfigurationStateStore<DeviceConfigurationEntity>>(
            sp => sp.GetRequiredService<IDeviceConfigurationStore>());
        services.AddSingleton<IQueuePublisher, AzureQueuePublisher>();
        services.AddSingleton<IAgentCommandPublisher, AgentCommandPublisher>();
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
        services.AddSingleton<IDeviceEventReader, AzureTableDeviceEventReader>();
        services.AddSingleton<IAgentEventReader, AzureTableAgentEventReader>();
        services.AddSingleton<IDeviceHeartbeatReader, AzureTableDeviceHeartbeatReader>();
        services.AddSingleton<IAgentHeartbeatReader, AzureTableAgentHeartbeatReader>();
        services.AddSingleton<IDeviceSnapshotStateReader, AzureTableDeviceSnapshotStateReader>();
        services.AddSingleton<ICameraCapturedHandler, CameraCapturedHandler>();
        services.AddSingleton<IClassifyRequestHandler, ClassifyRequestHandler>();
        services.AddSingleton<IDeviceEventQueueHandler, DeviceEventQueueHandler>();

        // Sprint 8 - operational alerting. The handler no-ops unless
        // OperationalAlert:Enabled is set, so registering it is safe
        // regardless of configuration.
        services.AddSingleton<IAgentAlertStateStore, AzureTableAgentAlertStateStore>();
        services.AddSingleton<IAgentAlertThrottle, AgentAlertThrottle>();
        services.AddSingleton<IAgentEventQueueHandler, AgentEventQueueHandler>();
        services.AddHttpClient<ITelegramService, TelegramService>();

        services.AddSingleton<IOfflineDetectionRule, OfflineDetectionRule>();
        services.AddSingleton<IRecoveryDetectionRule, RecoveryDetectionRule>();
        services.AddSingleton<IDeviceStatusResolver, DeviceStatusResolver>();
        services.AddSingleton<IAgentStatusResolver, AgentStatusResolver>();
        services.AddSingleton<INotificationChannel, TelegramNotificationChannel>();
        services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();
        services.AddSingleton<IHealthMonitorService, HealthMonitorService>();
        services.AddSingleton<IDeviceEventRetentionService, DeviceEventRetentionService>();
        services.AddSingleton<IAgentEventRetentionService, AgentEventRetentionService>();

        services.AddSingleton<IApiKeyStore, AzureTableApiKeyStore>();
        services.AddSingleton<IApiKeyAuthenticator, ApiKeyAuthenticator>();
        services.AddSingleton<IApiKeyManagementService, ApiKeyManagementService>();
        services.AddSingleton<IDeviceQueryService, DeviceQueryService>();
        services.AddSingleton<IAgentQueryService, AgentQueryService>();
        services.AddSingleton<IDeviceCapabilitiesQueryService, DeviceCapabilitiesQueryService>();

        services.AddSingleton<ICapabilityStore, AzureTableCapabilityStore>();
        services.AddSingleton<ICapabilityDependencyStore, AzureTableCapabilityDependencyStore>();
        services.AddSingleton<IDeviceTypeCapabilityStore, AzureTableDeviceTypeCapabilityStore>();
        services.AddSingleton<ICapabilityManagementService, CapabilityManagementService>();
        services.AddSingleton<ICapabilityConfigurationService, CapabilityConfigurationService>();
        services.AddSingleton<ICapabilityDependencyService, CapabilityDependencyService>();
        services.AddSingleton<ICapabilityCompatibilityService, CapabilityCompatibilityService>();

        services.AddSingleton<IAgentRegistryStore, AzureTableAgentRegistryStore>();
        services.AddSingleton<IAgentRegistryManagementService, AgentRegistryManagementService>();

        services.AddSingleton<IAgentCapabilityStore, AzureTableAgentCapabilityStore>();
        services.AddSingleton<IAgentCapabilityAssignmentService, AgentCapabilityAssignmentService>();

        services.AddSingleton<IDeviceTypeStore, AzureTableDeviceTypeStore>();
        services.AddSingleton<IDeviceTypeManagementService, DeviceTypeManagementService>();

        services.AddSingleton<IDeviceRegistryStore, AzureTableDeviceRegistryStore>();
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IDeviceRuntimeConfigurationProjector, DeviceRuntimeConfigurationProjector>();
        // The shared publish pipeline both publishers below sit on
        // (decision-log.md ADR-069/070). Open generic - it is closed
        // over exactly the two configuration entity types.
        services.AddSingleton(typeof(RuntimeConfigurationWriter<>));
        services.AddSingleton<IDeviceRuntimeConfigurationPublisher, DeviceRuntimeConfigurationPublisher>();
        services.AddSingleton<IAgentRuntimeConfigurationProjector, AgentRuntimeConfigurationProjector>();
        services.AddSingleton<IAgentRuntimeConfigurationPublisher, AgentRuntimeConfigurationPublisher>();

        // ICapabilityRuntimeProjector implementations (decision-log.md
        // ADR-064/ADR-065) - the shared registry both projectors above
        // take as IEnumerable<ICapabilityRuntimeProjector>. A capability
        // with no registered projector here simply isn't reflected in any
        // published document (a Warnings entry explains why) rather than
        // being guessed at.
        services.AddSingleton<ICapabilityRuntimeProjector, ImageCaptureRuntimeProjector>();
        services.AddSingleton<ICapabilityRuntimeProjector, ObjectDetectionRuntimeProjector>();
        services.AddSingleton<ICapabilityRuntimeProjector, SinkCleanlinessRuntimeProjector>();
        services.AddSingleton<ICapabilityRuntimeProjector, MotionDetectionRuntimeProjector>();

        // Catalogue self-seeding (ADR-119): reconciles stored catalogue
        // rows against the in-code CatalogueSeed manifest, taking the
        // same projector list so coverage gaps surface as warnings.
        services.AddSingleton<ICatalogueSeedService, CatalogueSeedService>();

        // Shared-config self-publishing (ADR-120): generates
        // shared-config/common-config.json from the in-code option
        // defaults instead of a hand-assembled blob.
        services.AddSingleton<ISharedConfigPublisher, SharedConfigPublisher>();
        services.AddSingleton<IConfigurationSyncStatusService, ConfigurationSyncStatusService>();

        services.AddSingleton<IDeviceCapabilityStore, AzureTableDeviceCapabilityStore>();
        services.AddSingleton<ICapabilityAssignmentService, CapabilityAssignmentService>();

        services.AddSingleton<ITenantStore, AzureTableTenantStore>();
        services.AddSingleton<ITenantManagementService, TenantManagementService>();

        services.AddSingleton<ISiteStore, AzureTableSiteStore>();
        services.AddSingleton<ISiteManagementService, SiteManagementService>();

        services.AddSingleton<IMachineStore, AzureTableMachineStore>();
        services.AddSingleton<IMachineManagementService, MachineManagementService>();

        services.AddSingleton<IAgentInstallationStore, AzureTableAgentInstallationStore>();
        services.AddSingleton<IInstallationTokenStore, AzureTableInstallationTokenStore>();
        services.AddSingleton<IInstallTokenService, InstallTokenService>();
        services.AddSingleton<IAgentInstallationManagementService, AgentInstallationManagementService>();
        services.AddSingleton<IAgentVersionStatusService, AgentVersionStatusService>();

        // Decision-log.md ADR-079 - Phase 9 Pass 1 (Command & Control).
        services.AddSingleton<IAgentCommandStore, AzureTableAgentCommandStore>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IAgentCommandManagementService, AgentCommandManagementService>();
        services.AddSingleton<ICommandExpiryService, CommandExpiryService>();

        return services;
    }
}
