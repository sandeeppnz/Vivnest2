using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Handlers;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Options;
using Vivnest.Cloud.Repositories;
using Vivnest.Cloud.Services;
using Vivnest.Cloud.Storage;
using Vivnest.Core.Options;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection("Telegram"));

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





builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
builder.Services.AddSingleton<IDeviceEventRepository, AzureTableDeviceEventRepository>();
builder.Services.AddSingleton<ICameraCapturedHandler, CameraCapturedHandler>();
builder.Services.AddHttpClient<ITelegramService, TelegramService>();



builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();





builder.Build().Run();
