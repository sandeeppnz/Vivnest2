using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Agent.Runtime.EventHandlers;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Agent.Runtime.Workers;
using Vivnest.Agent.Services;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.DataStores;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;
using Vivnest.Infrastructure.DataStores;
using Vivnest.Infrastructure.DependencyInjection;
using Vivnest.Infrastructure.Utils;


var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<MessagingOptions>(
    builder.Configuration.GetSection("Messaging"));

builder.Services.AddSingleton(sp =>
{
    var options = sp
        .GetRequiredService<IOptions<MessagingOptions>>()
        .Value;

    return new QueueServiceClient(options.ConnectionString);
});

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.Configure<DevicesOptions>(
    builder.Configuration);

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("Agent"));

builder.Services.AddSingleton(sp =>
{
    var options = sp
        .GetRequiredService<IOptions<StorageOptions>>()
        .Value;

    return new TableServiceClient(options.ConnectionString);
});

builder.Services.Configure<AgentHeartbeatOptions>(
    builder.Configuration.GetSection("AgentHeartbeat"));

builder.Services.Configure<TablesOptions>(
    builder.Configuration.GetSection("Tables"));


builder.Services.Configure<DeviceEventOptions>(
    builder.Configuration.GetSection("DeviceEvents"));

builder.Services.Configure<DeviceHeartbeatOptions>(
    builder.Configuration.GetSection("DeviceHeartbeat"));




builder.Services.AddInfrastructure();

builder.Services.AddSingleton<IBlobNameGenerator, BlobNameGenerator>();
builder.Services.AddSingleton<IAgentHeartbeatStore, AgentHeartbeatStore>();
builder.Services.AddSingleton<IDeviceHeartbeatStore, DeviceHeartbeatStore>();
builder.Services.AddSingleton<IDeviceEventStore, AzureTableDeviceEventStore>();
builder.Services.AddSingleton<IEventDispatcher, EventDispatcher>();

builder.Services.AddSingleton<IEventHandler<AgentHeartbeatGeneratedEvent>, AgentHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<DeviceHeartbeatGeneratedEvent>, DeviceHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<CameraCaptureCompletedEvent>, CameraCaptureHandler>();
builder.Services.AddSingleton<IEventHandler<CameraCaptureFailedEvent>, CameraCaptureFailedHandler>();


builder.Services.AddSingleton<ICameraCaptureService, CameraCaptureService>();
builder.Services.AddSingleton<IDeviceRuntimeStore, DeviceRegistry>();
builder.Services.AddSingleton<ICaptureStatusStore, CaptureStatusStore>();

builder.Services.AddHostedService<CameraCaptureWorker>();
builder.Services.AddHostedService<AgentHeartbeatWorker>();
builder.Services.AddHostedService<DeviceHeartbeatWorker>();

var app = builder.Build();

await app.RunAsync();