using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin;
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
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Storage;

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
        services.AddHttpClient<ITelegramService, TelegramService>();

        services.AddSingleton<IOfflineDetectionRule, OfflineDetectionRule>();
        services.AddSingleton<IRecoveryDetectionRule, RecoveryDetectionRule>();
        services.AddSingleton<IDeviceStatusResolver, DeviceStatusResolver>();
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
        services.AddSingleton<ICapabilityManagementService, CapabilityManagementService>();

        services.AddSingleton<IAgentRegistryStore, AzureTableAgentRegistryStore>();
        services.AddSingleton<IAgentRegistryManagementService, AgentRegistryManagementService>();

        services.AddSingleton<IDeviceTypeStore, AzureTableDeviceTypeStore>();
        services.AddSingleton<IDeviceTypeManagementService, DeviceTypeManagementService>();

        services.AddSingleton<IDeviceRegistryStore, AzureTableDeviceRegistryStore>();
        services.AddSingleton<IDeviceRegistryManagementService, DeviceRegistryManagementService>();

        services.AddSingleton<ITenantStore, AzureTableTenantStore>();
        services.AddSingleton<ITenantManagementService, TenantManagementService>();

        services.AddSingleton<ISiteStore, AzureTableSiteStore>();
        services.AddSingleton<ISiteManagementService, SiteManagementService>();

        return services;
    }
}
