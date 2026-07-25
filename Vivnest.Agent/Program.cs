using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vivnest.Agent.Services;
using Vivnest.Agent.Workers;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models;
using Vivnest.Core.Options;
using Vivnest.Infrastructure.DependencyInjection;
using Vivnest.Infrastructure.Services;
using Vivnest.Infrastructure.Stores;


var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.Configure<DevicesOptions>(
    builder.Configuration);

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("Agent"));

builder.Services.Configure<GatewayOptions>(
    builder.Configuration.GetSection("Gateway"));

builder.Services.Configure<HeartbeatOptions>(
    builder.Configuration.GetSection("Heartbeat"));

builder.Services.Configure<DeviceEventOptions>(
    builder.Configuration.GetSection("DeviceEvents"));


builder.Services.AddSingleton(sp =>
{
    var connectionString =
        builder.Configuration.GetConnectionString("Storage");
    return new QueueServiceClient(connectionString);
});

builder.Services.AddInfrastructure();

//TODO: move to infra
builder.Services.AddSingleton<IBlobNameGenerator, BlobNameGenerator>(); 
builder.Services.AddSingleton<IHeartbeatStore, AzureTableHeartbeatStore>();
builder.Services.AddSingleton<IDeviceEventStore, AzureTableDeviceEventStore>();

//if (gatewayOptions.Mode == "Local")
//{
builder.Services.AddSingleton<IAgentGateway, AgentGatewayService>();
//}

builder.Services.AddSingleton<ICaptureService, CaptureService>();
builder.Services.AddSingleton<IDeviceRegistry, DeviceRegistry>();

builder.Services.AddHostedService<CaptureWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();


var app = builder.Build();

await app.RunAsync();