using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Handlers;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Cloud.Repositories;
using Vivnest.Cloud.Rules;
using Vivnest.Cloud.Services;
using Vivnest.Cloud.Storage;
using Vivnest.Core.Options;
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

        services.AddSingleton<AzureBlobStorageClient>();
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
        services.AddSingleton<IDeviceEventReader, AzureTableDeviceEventReader>();
        services.AddSingleton<IDeviceHeartbeatReader, AzureTableDeviceHeartbeatReader>();
        services.AddSingleton<IAgentHeartbeatReader, AzureTableAgentHeartbeatReader>();
        services.AddSingleton<IDeviceSnapshotStateReader, AzureTableDeviceSnapshotStateReader>();
        services.AddSingleton<ICameraCapturedHandler, CameraCapturedHandler>();
        services.AddSingleton<IDeviceEventQueueHandler, DeviceEventQueueHandler>();
        services.AddHttpClient<ITelegramService, TelegramService>();

        services.AddSingleton<IOfflineDetectionRule, OfflineDetectionRule>();
        services.AddSingleton<IRecoveryDetectionRule, RecoveryDetectionRule>();
        services.AddSingleton<INotificationChannel, TelegramNotificationChannel>();
        services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();
        services.AddSingleton<IHealthMonitorService, HealthMonitorService>();

        services.AddSingleton<IApiKeyStore, AzureTableApiKeyStore>();
        services.AddSingleton<IApiKeyAuthenticator, ApiKeyAuthenticator>();
        services.AddSingleton<IApiKeyManagementService, ApiKeyManagementService>();
        services.AddSingleton<IDeviceQueryService, DeviceQueryService>();
        services.AddSingleton<IAgentQueryService, AgentQueryService>();

        return services;
    }
}
