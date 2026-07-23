using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Register services here
// builder.Services.Configure<AgentOptions>(...);
// builder.Services.AddSingleton<ICamera, TapoCamera>();
// builder.Services.AddHostedService<CaptureWorker>();

var app = builder.Build();

await app.RunAsync();