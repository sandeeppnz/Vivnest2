using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vivnest.Agent.Bootstrap;
using Vivnest.Agent.Configuration;
using Vivnest.Runtime.Capabilities;
using Vivnest.Core.Capabilities;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<
    AgentCapabilityAssignmentFactory>();

builder.Services.AddSingleton<
    IRuntimeCapabilityAssignmentStore>(sp =>
    {
        var factory =
            sp.GetRequiredService<
                AgentCapabilityAssignmentFactory>();

        var assignments = factory.Create();

        return new RuntimeCapabilityAssignmentStore(
            assignments);
    });

await AgentBootstrap.ConfigureAsync(builder);

var app = builder.Build();

await app.RunAsync();