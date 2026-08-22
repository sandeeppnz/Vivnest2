using Microsoft.Extensions.Hosting;
using Vivnest.Agent.Bootstrap;

var builder = Host.CreateApplicationBuilder(args);

await AgentBootstrap.ConfigureAsync(builder);

var app = builder.Build();

await app.RunAsync();