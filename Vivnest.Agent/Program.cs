using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vivnest.Agent.Services;
using Vivnest.Agent.Workers;
using Vivnest.Core.Heartbeat;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.DependencyInjection;
using Vivnest.Infrastructure.Heartbeat;
using Vivnest.Infrastructure.Storage;


var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.Configure<CameraOptions>(
    builder.Configuration.GetSection("Camera"));

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("Agent"));

builder.Services.Configure<HeartbeatOptions>(
    builder.Configuration.GetSection("Heartbeat"));

builder.Services.AddInfrastructure();

builder.Services.AddSingleton<IBlobNameGenerator, BlobNameGenerator>(); 
builder.Services.AddSingleton<IHeartbeatService, AzureTableHeartbeatService>();
builder.Services.AddSingleton<ICaptureService, CaptureService>();

builder.Services.AddHostedService<CaptureWorker>();


var app = builder.Build();

await app.RunAsync();