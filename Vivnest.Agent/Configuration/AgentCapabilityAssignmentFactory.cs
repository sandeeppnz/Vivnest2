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
        // Bind the "Capabilities" section as the LIST it is.
        //
        // This used to be GetSection("Capabilities").Bind(options), which
        // silently produced zero assignments every time: the publisher
        // writes root["Capabilities"] = [ ... ], so that section's children
        // are the array indices "0", "1", ... and binding them onto an
        // object whose list property is also called Capabilities looks for
        // a child named "Capabilities" that does not exist. No error, no
        // warning - just an empty list, which reads downstream as "Cloud
        // assigned nothing" and stops every capability from starting.
        var assignments =
            _configuration
                .GetSection("Capabilities")
                .Get<List<AgentCapabilityOption>>()
            ?? [];

        return assignments
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.CapabilityId))
            .Select(x =>
                new RuntimeCapabilityAssignment
                {
                    CapabilityId = x.CapabilityKey,
                    CapabilityName = x.Name,
                    Enabled = x.Enabled
                })
            .ToList();
    }
}
