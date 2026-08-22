using Microsoft.Extensions.Configuration;
using Vivnest.Runtime.Capabilities;

namespace Vivnest.Agent.Configuration;

public sealed class AgentCapabilityAssignmentFactory
{
    private readonly IConfiguration _configuration;

    public AgentCapabilityAssignmentFactory(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IReadOnlyCollection<RuntimeCapabilityAssignment>
        Create()
    {
        var options = new AgentCapabilityOptions();

        _configuration
            .GetSection("Capabilities")
            .Bind(options);

        return options.Capabilities
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.CapabilityId))
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
