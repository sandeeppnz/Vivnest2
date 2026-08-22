using Microsoft.Extensions.Configuration;
using Vivnest.Runtime.Capabilities;

namespace Vivnest.Agent.Configuration;

public sealed class AgentCapabilityConfigurationLoader
{
    private readonly IConfiguration _configuration;

    public AgentCapabilityConfigurationLoader(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IReadOnlyList<RuntimeCapabilityAssignment> Load()
    {
        var options = new AgentCapabilityOptions();

        _configuration
            .GetSection("Capabilities")
            .Bind(options);

        return options.Capabilities
            .Where(x => !string.IsNullOrWhiteSpace(x.CapabilityId))
            .Select(x =>
                new RuntimeCapabilityAssignment
                {
                    CapabilityId = x.CapabilityId,
                    CapabilityName = x.Name,
                    Enabled = x.Enabled
                })
            .ToList();
    }
}