using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Vivnest.Abstraction.Agent.Capabilities;
using Vivnest.Runtime.Capabilities;

namespace Vivnest.Agent.Tests;

// ADR-103, the half of the decision that lives above the capability:
//
//     Failed  !=  Agent stopped
//
// An Agent reporting
//
//     camera.capture:     Failed
//     motion.sensor:      Running
//     smartplug.monitor:  Running
//
// is strictly more useful than a container that quietly bounces, because
// the second one erases the difference between "one capability died" and
// "the Agent died". The first cut of this fix called StopApplication() on
// a worker fault, which was auto-restart - unbounded, no backoff - wearing
// the clothes of a status fix. This pins the corrected behaviour.
//
// Note what is NOT asserted here: that a capability which THROWS from
// StartAsync leaves the Agent up. It does not - CapabilityHost rethrows,
// deliberately, and that is still ADR-095's open fault-isolation question.
// The scope of ADR-103 is a worker that dies AFTER a successful start.
public class CapabilityHostFaultIsolationTests
{
    [Fact]
    public async Task AFailedCapabilityDoesNotPreventTheOthersFromStarting()
    {
        var failing = new StubCapability("camera.capture", failsOnStart: true);
        var healthyA = new StubCapability("motion.sensor");
        var healthyB = new StubCapability("smartplug.monitor");

        var host = BuildHost(failing, healthyA, healthyB);

        // The host completing at all is half the assertion: it neither
        // threw nor took the process with it.
        await host.StartAsync(CancellationToken.None);

        Assert.Equal(CapabilityStatus.Failed, failing.Status);
        Assert.Equal(CapabilityStatus.Running, healthyA.Status);
        Assert.Equal(CapabilityStatus.Running, healthyB.Status);
    }

    // Structural, because a test cannot easily prove the absence of a
    // shutdown. If the supervisor ever regains the ability to stop the
    // host, it has to come back through this constructor first.
    [Fact]
    public void TheSupervisorCannotStopTheHost()
    {
        var parameters = typeof(CapabilityWorkerSupervisor)
            .GetMethod(nameof(CapabilityWorkerSupervisor.Observe))!
            .GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToList();

        Assert.DoesNotContain("IHostApplicationLifetime", parameters);
    }

    // =======================================================================

    private static CapabilityHost BuildHost(params ICapability[] capabilities) =>
        new(
            new CapabilityRegistry(capabilities),
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Agent:AgentId"] = "agent-1" })
                .Build(),
            new EmptyServiceProvider(),
            new StubAssignmentStore(capabilities.Select(c => c.Manifest.Id)),
            NullLogger<CapabilityHost>.Instance);

    // Fails the way a worker fault does - by SETTING Failed and returning
    // normally, not by throwing. That distinction is the whole point: a
    // throw would reach CapabilityHost's catch and rethrow, which is a
    // different (still open) decision.
    private sealed class StubCapability(string id, bool failsOnStart = false) : ICapability
    {
        private CapabilityStatus _status = CapabilityStatus.Registered;

        public CapabilityStatus Status => _status;

        public CapabilityManifest Manifest => new()
        {
            Id = id,
            Name = id,
            Version = "1.0.0"
        };

        public Task StartAsync(ICapabilityContext context, CancellationToken ct)
        {
            _status = failsOnStart
                ? CapabilityStatus.Failed
                : CapabilityStatus.Running;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            _status = CapabilityStatus.Stopped;

            return Task.CompletedTask;
        }
    }

    private sealed class StubAssignmentStore(IEnumerable<string> capabilityIds)
        : IRuntimeCapabilityAssignmentStore
    {
        private readonly List<RuntimeCapabilityAssignment> _assignments =
            capabilityIds
                .Select(x => new RuntimeCapabilityAssignment
                {
                    CapabilityId = x,
                    CapabilityName = x,
                    Enabled = true
                })
                .ToList();

        public IReadOnlyCollection<RuntimeCapabilityAssignment> GetAll() => _assignments;

        public IReadOnlyCollection<RuntimeCapabilityAssignment> GetEnabled() => _assignments;

        public RuntimeCapabilityAssignment? Get(string capabilityId) =>
            _assignments.FirstOrDefault(x => x.CapabilityId == capabilityId);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
