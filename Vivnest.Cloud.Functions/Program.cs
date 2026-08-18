using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vivnest.Cloud.DependencyInjection;
using Vivnest.Cloud.Options;
using Vivnest.Core.Options;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection("Telegram"));

builder.Services.Configure<TablesOptions>(
    builder.Configuration.GetSection("Tables"));

builder.Services.Configure<DeviceEventOptions>(
    builder.Configuration.GetSection("DeviceEvents"));

builder.Services.Configure<AgentEventOptions>(
    builder.Configuration.GetSection("AgentEvents"));

builder.Services.Configure<HealthMonitorOptions>(
    builder.Configuration.GetSection("HealthMonitor"));

builder.Services.Configure<SnapshotNotificationOptions>(
    builder.Configuration.GetSection("SnapshotNotification"));

builder.Services.Configure<SinkCleanlinessNotificationOptions>(
    builder.Configuration.GetSection("SinkCleanlinessNotification"));

builder.Services.Configure<DeviceEventRetentionOptions>(
    builder.Configuration.GetSection("DeviceEventRetention"));

builder.Services.Configure<AgentEventRetentionOptions>(
    builder.Configuration.GetSection("AgentEventRetention"));

builder.Services.Configure<CommandExpiryOptions>(
    builder.Configuration.GetSection("CommandExpiry"));

builder.Services.Configure<CredentialEncryptionOptions>(
    builder.Configuration.GetSection("CredentialEncryption"));

builder.Services.Configure<AgentAuthOptions>(
    builder.Configuration.GetSection("AgentAuth"));

builder.Services.AddCloud();

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
