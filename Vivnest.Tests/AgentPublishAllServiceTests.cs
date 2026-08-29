using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Entities;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;

namespace Vivnest.Tests;

// "Publish all & refresh" (ADR-121). What matters here is the sequencing
// contract: devices before agent before refresh, one bad item never
// stopping the rest, and the refresh always going out - the properties
// that make the action safe to use as THE way to bring an agent in line.
public class AgentPublishAllServiceTests
{
    private const string RuntimeAgentId = "agent-low-01";
    private const string RegistryAgentId = "11111111-1111-1111-1111-111111111111";

    private static readonly TenantContext Tenant =
        new("tenant-1", "site-1", DevicesOnly: false);

    [Fact]
    public async Task UnknownRuntimeAgentReturnsNull()
    {
        var service = Build(out _, out _, out _, out var dispatcher, agentRegistered: false);

        Assert.Null(await service.PublishAllAsync(Tenant, RuntimeAgentId, "tester"));
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public async Task PublishesActiveDevicesThenAgentThenRefreshes()
    {
        var service = Build(out var devices, out var devicePublisher, out _, out var dispatcher);
        devices.Rows.Add(Device("camera-001", "Active"));
        devices.Rows.Add(Device("plug-001", "Disabled"));

        var report = await service.PublishAllAsync(Tenant, RuntimeAgentId, "tester");

        Assert.NotNull(report);
        Assert.Equal(
            ["device:camera-001:published", "device:plug-001:skipped", "agent:Low agent:published", "refresh:Low agent:queued"],
            report!.Items.Select(i => $"{i.Kind}:{i.Name}:{i.Outcome}").ToArray());

        Assert.Equal(["camera-001"], devicePublisher.PublishedDeviceNames);
        Assert.True(report.RefreshQueued);
        Assert.Equal([(AgentCommandTypes.RefreshConfiguration, RuntimeAgentId, "tester")], dispatcher.Dispatched);
    }

    [Fact]
    public async Task UnchangedAndBlockedAreDistinguishedAndRefreshStillGoesOut()
    {
        var service = Build(out var devices, out var devicePublisher, out _, out _);
        devices.Rows.Add(Device("camera-001", "Active"));
        devices.Rows.Add(Device("motion-001", "Active"));
        devicePublisher.ReasonByName["camera-001"] = "Configuration unchanged since version 4.";
        devicePublisher.ReasonByName["motion-001"] = "Cannot publish: OwningAgentId doesn't exist.";

        var report = await service.PublishAllAsync(Tenant, RuntimeAgentId, "tester");

        Assert.Equal("unchanged", report!.Items.Single(i => i.Name == "camera-001").Outcome);
        Assert.Equal("blocked", report.Items.Single(i => i.Name == "motion-001").Outcome);
        Assert.True(report.RefreshQueued);
    }

    [Fact]
    public async Task OneThrowingDevicePublishDoesNotStopTheSequence()
    {
        var service = Build(out var devices, out var devicePublisher, out var agentPublisher, out _);
        devices.Rows.Add(Device("camera-001", "Active"));
        devices.Rows.Add(Device("motion-001", "Active"));
        devicePublisher.ThrowFor = "camera-001";

        var report = await service.PublishAllAsync(Tenant, RuntimeAgentId, "tester");

        Assert.Equal("failed", report!.Items.Single(i => i.Name == "camera-001").Outcome);
        Assert.Equal("published", report.Items.Single(i => i.Name == "motion-001").Outcome);
        Assert.True(agentPublisher.Published);
        Assert.True(report.RefreshQueued);
    }

    [Fact]
    public async Task RefreshFailureIsReportedNotThrown()
    {
        var service = Build(out _, out _, out _, out var dispatcher);
        dispatcher.ReturnNull = true;

        var report = await service.PublishAllAsync(Tenant, RuntimeAgentId, "tester");

        Assert.False(report!.RefreshQueued);
        Assert.Equal("failed", report.Items.Single(i => i.Kind == "refresh").Outcome);
    }

    // ---------------------------------------------------------------

    private static AgentPublishAllService Build(
        out FakeDeviceService devices,
        out FakeDevicePublisher devicePublisher,
        out FakeAgentPublisher agentPublisher,
        out FakeDispatcher dispatcher,
        bool agentRegistered = true)
    {
        var registry = new FakeRegistryStore
        {
            Row = !agentRegistered ? null : new AgentRegistryEntity
            {
                PartitionKey = "tenant-1|site-1",
                RowKey = RegistryAgentId,
                TenantId = "tenant-1",
                SiteId = "site-1",
                Name = "Low agent",
                RuntimeAgentId = RuntimeAgentId,
            },
        };

        devices = new FakeDeviceService();
        devicePublisher = new FakeDevicePublisher();
        agentPublisher = new FakeAgentPublisher();
        dispatcher = new FakeDispatcher();

        var rows = devices.Rows;
        devicePublisher.NameResolver = id =>
            rows.FirstOrDefault(r => r.DeviceId.ToString() == id)?.Name ?? id;

        return new AgentPublishAllService(registry, devices, devicePublisher, agentPublisher, dispatcher);
    }

    private static DeviceRegistryDto Device(string name, string status) =>
        new(Guid.NewGuid(), name, "type-1", RegistryAgentId, "", "", "", "", status,
            name, new Dictionary<string, string>(), "tenant-1", "site-1");

    private sealed class FakeRegistryStore : IAgentRegistryStore
    {
        public AgentRegistryEntity? Row { get; set; }

        public Task<AgentRegistryEntity?> GetByRuntimeAgentIdAsync(
            string tenantId, string siteId, string runtimeAgentId, CancellationToken ct = default) =>
            Task.FromResult(Row?.RuntimeAgentId == runtimeAgentId ? Row : null);

        public Task<IReadOnlyList<AgentRegistryEntity>> ListAsync(
            string tenantId, string siteId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AgentRegistryEntity?> GetAsync(
            string tenantId, string siteId, string agentId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CreateAsync(AgentRegistryEntity entity, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(AgentRegistryEntity entity, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string tenantId, string siteId, string agentId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDeviceService : IDeviceService
    {
        public List<DeviceRegistryDto> Rows { get; } = [];

        public Task<IReadOnlyList<DeviceRegistryDto>> ListAsync(
            TenantContext tenant, string? ownerAgentId = null, string? deviceTypeId = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceRegistryDto>>(
                Rows.Where(r => ownerAgentId == null || r.OwningAgentId == ownerAgentId).ToList());

        public Task<DeviceRegistryDto?> CreateAsync(
            TenantContext tenant, string name, string deviceTypeId, string owningAgentId,
            string location, string brand, string model, string firmware, string? runtimeDeviceId,
            IReadOnlyDictionary<string, string>? settings, int? livenessIntervalSeconds = null,
            double? warningMultiplier = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DeviceRegistryDto?> UpdateAsync(
            TenantContext tenant, string deviceId, string name, string deviceTypeId,
            string owningAgentId, string location, string brand, string model, string firmware,
            string status, string? runtimeDeviceId, IReadOnlyDictionary<string, string>? settings,
            int? livenessIntervalSeconds = null, double? warningMultiplier = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDevicePublisher : IDeviceRuntimeConfigurationPublisher
    {
        public Dictionary<string, string> ReasonByName { get; } = [];
        public List<string> PublishedDeviceNames { get; } = [];
        public string? ThrowFor { get; set; }

        // Wired to the FakeDeviceService rows in Build - the service
        // publishes by DeviceId, the assertions read by Name.
        public Func<string, string> NameResolver { get; set; } = id => id;

        public Task<DevicePublishResult?> PublishAsync(
            TenantContext tenant, string deviceId, CancellationToken ct = default)
        {
            var name = NameResolver(deviceId);

            if (name == ThrowFor)
                throw new InvalidOperationException("storage exploded");

            if (ReasonByName.TryGetValue(name, out var reason))
                return Task.FromResult<DevicePublishResult?>(new DevicePublishResult(false, null!, reason));

            PublishedDeviceNames.Add(name);
            // The service only reads Published/Reason - Document is never
            // dereferenced, so the fake doesn't build one.
            return Task.FromResult<DevicePublishResult?>(new DevicePublishResult(true, null!, null));
        }

        public Task<DevicePublishResult?> RollbackAsync(
            TenantContext tenant, string deviceId, int targetVersion, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAgentPublisher : IAgentRuntimeConfigurationPublisher
    {
        public bool Published { get; private set; }

        public Task<AgentPublishResult?> PublishAsync(
            TenantContext tenant, string agentId, CancellationToken ct = default)
        {
            Published = true;
            return Task.FromResult<AgentPublishResult?>(new AgentPublishResult(true, null!, null));
        }

        public Task<AgentPublishResult?> RollbackAsync(
            TenantContext tenant, string agentId, int targetVersion, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDispatcher : ICommandDispatcher
    {
        public List<(string Type, string AgentId, string RequestedBy)> Dispatched { get; } = [];
        public bool ReturnNull { get; set; }

        public Task<AgentCommandDto?> DispatchAsync(
            TenantContext tenant, string commandType, string targetAgentId, string requestedBy,
            string? targetDeviceId = null, string? capabilityId = null, string? payload = null,
            CancellationToken ct = default)
        {
            if (ReturnNull)
                return Task.FromResult<AgentCommandDto?>(null);

            Dispatched.Add((commandType, targetAgentId, requestedBy));

            return Task.FromResult<AgentCommandDto?>(new AgentCommandDto(
                Guid.NewGuid().ToString(), targetAgentId, targetDeviceId, capabilityId,
                commandType, "Pending", payload, null, null, null, requestedBy,
                DateTime.UtcNow, null, null, null, null,
                DateTime.UtcNow.AddMinutes(10), "tenant-1", "site-1"));
        }
    }
}
