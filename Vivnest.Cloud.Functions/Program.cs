using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Handlers;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Cloud.Options;
using Vivnest.Cloud.Repositories;
using Vivnest.Cloud.Rules;
using Vivnest.Cloud.Services;
using Vivnest.Cloud.Storage;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection("Telegram"));

builder.Services.Configure<TablesOptions>(
    builder.Configuration.GetSection("Tables"));

builder.Services.Configure<DeviceEventOptions>(
    builder.Configuration.GetSection("DeviceEvents"));

builder.Services.Configure<HealthMonitorOptions>(
    builder.Configuration.GetSection("HealthMonitor"));

builder.Services.AddSingleton(sp =>
{
    var options = sp
        .GetRequiredService<IOptions<StorageOptions>>()
        .Value;

    return new TableServiceClient(options.ConnectionString);
});


builder.Services.AddSingleton(sp =>
{
    var options = sp
        .GetRequiredService<IOptions<StorageOptions>>()
        .Value;

    return new BlobServiceClient(options.ConnectionString);
});





builder.Services.AddSingleton<AzureBlobStorageClient>();
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
builder.Services.AddSingleton<IDeviceEventReader, AzureTableDeviceEventReader>();
builder.Services.AddSingleton<ICameraCapturedHandler, CameraCapturedHandler>();
builder.Services.AddHttpClient<ITelegramService, TelegramService>();

builder.Services.AddSingleton<IDeviceHeartbeatReader, AzureTableDeviceHeartbeatReader>();
builder.Services.AddSingleton<IAgentHeartbeatReader, AzureTableAgentHeartbeatReader>();
builder.Services.AddSingleton<IOfflineDetectionRule, OfflineDetectionRule>();
builder.Services.AddSingleton<IRecoveryDetectionRule, RecoveryDetectionRule>();
builder.Services.AddSingleton<INotificationChannel, TelegramNotificationChannel>();
builder.Services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddSingleton<IHealthMonitorService, HealthMonitorService>();



builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();





builder.Build().Run();
