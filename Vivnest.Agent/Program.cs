using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Services;
using Vivnest.Agent.Workers;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Interfaces.Stores;
using Vivnest.Core.Models;
using Vivnest.Core.Options;
using Vivnest.Core.Options.Heartbeats;
using Vivnest.Infrastructure.DependencyInjection;
using Vivnest.Infrastructure.Services;
using Vivnest.Infrastructure.Stores;
using Vivnest.Infrastructure.Stores.Heartbeats;


var builder = Host.CreateApplicationBuilder(args);



builder.Services.Configure<MessagingOptions>(
    builder.Configuration.GetSection("Messaging"));

builder.Services.AddSingleton(sp =>
{
    var options = builder.Configuration
     .GetSection("Messaging")
     .Get<MessagingOptions>();

    var storage = sp
           .GetRequiredService<IOptions<MessagingOptions>>()
           .Value;

    return new QueueServiceClient(storage.ConnectionString);
});

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.Configure<DevicesOptions>(
    builder.Configuration);

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("Agent"));

builder.Services.AddSingleton(_ =>
{
    var options = builder.Configuration
        .GetSection("Storage")
        .Get<StorageOptions>();

    return new TableServiceClient(options.ConnectionString);
});

builder.Services.Configure<AgentHeartbeatOptions>(
    builder.Configuration.GetSection("AgentHeartbeat"));

builder.Services.Configure<TablesOptions>(
    builder.Configuration.GetSection("Tables"));


builder.Services.Configure<DeviceEventOptions>(
    builder.Configuration.GetSection("DeviceEvents"));



builder.Services.AddInfrastructure();

builder.Services.AddSingleton<IBlobNameGenerator, BlobNameGenerator>();
builder.Services.AddSingleton<IAgentHeartbeatStore, AgentHeartbeatStore>();
builder.Services.AddSingleton<IDeviceHeartbeatStore, DeviceHeartbeatStore>();
builder.Services.AddSingleton<IDeviceEventStore, AzureTableDeviceEventStore>();
builder.Services.AddSingleton<IAgentGateway, AgentGatewayService>();
builder.Services.AddSingleton<ICaptureService, CaptureService>();
builder.Services.AddSingleton<IDeviceRegistry, DeviceRegistry>();


builder.Services.AddHostedService<CaptureWorker>();
builder.Services.AddHostedService<AgentHeartbeatWorker>();


var app = builder.Build();

await app.RunAsync();