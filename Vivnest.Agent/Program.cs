using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vivnest.Agent.Services;
using Vivnest.Core.Options;
using Vivnest.Infrastructure.DependencyInjection;


var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.Configure<CameraOptions>(
    builder.Configuration.GetSection("Camera"));

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("Agent"));

builder.Services.AddInfrastructure();
builder.Services.AddHostedService<UploadTestService>();

var app = builder.Build();

await app.RunAsync();