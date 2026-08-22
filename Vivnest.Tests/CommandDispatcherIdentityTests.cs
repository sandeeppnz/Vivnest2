using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Tests;

// ADR-102 (Command Routing 1.5), driven through the real CommandDispatcher
// rather than its extracted rule - the wiring is the part that can be wrong.
//
// The separation being proved: the caller's requested identity reaches
// AUTHORIZATION unchanged, while a separately RESOLVED catalogue id is what
// gets persisted. Coupling those two would silently move "ImageCapture"
// from the built-in ownership check onto the derived
// DeviceCapability/ExecutingAgentId check - an authorization change wearing
// a rename's clothing.
public class CommandDispatcherIdentityTests
{
    private const string CatalogueGuid = "5217f0ef-f7c6-4d9f-9723-7bf2afad5572";
    private const string RuntimeAgentId = "agent-runtime-1";
    private const string DeviceId = "device-1";

    private static readonly TenantContext Tenant = new("tenant-1", "site-1", DevicesOnly: false);

    // ---- Test A: a catalogue id is persisted unchanged -------------------
    [Fact]
    public async Task ACatalogueIdIsPersistedUnchanged()
    {
        var h = new Harness();

        await h.DispatchExecuteCapabilityAsync(CatalogueGuid);

        Assert.Equal(CatalogueGuid, h.Commands.Created!.CapabilityId);
    }

    // ---- Test B: the legacy alias resolves -------------------------------
    // The most important new behaviour: "ImageCapture" is neither a
    // catalogue RowKey nor a CapabilityKey, and must not reach storage.
    [Fact]
    public async Task LegacyImageCaptureIsPersistedAsTheCatalogueId()
    {
        var h = new Harness();

        await h.DispatchExecuteCapabilityAsync(AgentCommandTypes.ImageCaptureCapabilityId);

        Assert.Equal(CatalogueGuid, h.Commands.Created!.CapabilityId);
        Assert.NotEqual("ImageCapture", h.Commands.Created.CapabilityId);
    }

    // ---- Test C: only the established convention -------------------------
    // RuntimeNameMatch normalizes whitespace and case, and explicitly
    // nothing else. Testing beyond that would pin behaviour the rule never
    // claimed.
    [Theory]
    [InlineData("ImageCapture")]
    [InlineData("imagecapture")]
    [InlineData("Image Capture")]
    [InlineData("IMAGE CAPTURE")]
    public async Task SpacingAndCaseVariantsResolveToTheCatalogueId(string supplied)
    {
        var h = new Harness();

        await h.DispatchExecuteCapabilityAsync(supplied);

        Assert.Equal(CatalogueGuid, h.Commands.Created!.CapabilityId);
    }

    // ---- Test D: authorization saw the ORIGINAL value --------------------
    // The whole point of resolving after validation. If these two ever
    // become the same value, the built-in path has quietly changed which
    // authorization branch it takes.
    [Fact]
    public async Task ValidationSeesTheRequestedIdentityNotTheResolvedOne()
    {
        var h = new Harness();

        var result = await h.DispatchExecuteCapabilityAsync("ImageCapture");

        // The built-in branch is ownership-only: it never consults
        // DeviceCapability. Reaching it is therefore observable as the
        // absence of a derived lookup - and it is only reachable if
        // ValidateAsync was handed the caller's literal, since the resolved
        // value is a GUID.
        Assert.Empty(h.DeviceCapabilities.DerivedLookups);
        Assert.Null(result!.ErrorCode);

        // ...while what got stored is the resolved catalogue id.
        Assert.Equal(CatalogueGuid, h.Commands.Created!.CapabilityId);
    }

    // The other side of the same coin: a catalogue GUID takes the DERIVED
    // branch, which does consult DeviceCapability. If resolution ever ran
    // before validation, the previous test would look like this one.
    [Fact]
    public async Task ACatalogueIdTakesTheDerivedValidationBranch()
    {
        var h = new Harness();

        await h.DispatchExecuteCapabilityAsync(CatalogueGuid);

        Assert.Equal([CatalogueGuid], h.DeviceCapabilities.DerivedLookups);
    }

    // ---- Test E: an unknown identity is not silently accepted ------------
    // It is still recorded, as a Failed row - that is this dispatcher's
    // deliberate convention (see DispatchAsync: a rejected command is
    // created in its terminal state rather than thrown away, so the caller
    // can read why). What must not happen is a Dispatched command carrying
    // a capability nothing can route.
    [Fact]
    public async Task AnUnknownIdentityIsRejectedRatherThanDispatched()
    {
        var h = new Harness();

        var result = await h.DispatchExecuteCapabilityAsync("does.not.exist");

        Assert.NotNull(result);
        Assert.NotNull(result!.ErrorCode);
        Assert.NotEqual("Dispatched", h.Commands.Created!.Status);
        Assert.Empty(h.Publisher.Published);
    }

    // =====================================================================
    // Test doubles. Private to this class - they exist to drive one
    // boundary, not to become a shared fake nobody owns.
    // =====================================================================

    private sealed class Harness
    {
        public StubCommandStore Commands { get; } = new();
        public StubPublisher Publisher { get; } = new();
        public StubDevices Devices { get; } = new();
        public StubDeviceCapabilities DeviceCapabilities { get; } = new();
        public CommandDispatcher Dispatcher { get; }

        public Harness()
        {
            Dispatcher = new CommandDispatcher(
                Commands,
                Publisher,
                new StubAgents(),
                Devices,
                DeviceCapabilities,
                new StubAgentRegistry(),
                new StubAgentConfigurations(),
                new StubCapabilities());
        }

        public Task<AgentCommandDto?> DispatchExecuteCapabilityAsync(string capabilityId) =>
            Dispatcher.DispatchAsync(
                Tenant,
                AgentCommandTypes.ExecuteCapability,
                RuntimeAgentId,
                requestedBy: "test",
                targetDeviceId: DeviceId,
                capabilityId: capabilityId);
    }

    private sealed class StubCapabilities : ICapabilityStore
    {
        private readonly List<CapabilityEntity> _items =
        [
            new()
            {
                PartitionKey = "capability",
                RowKey = CatalogueGuid,
                CapabilityName = "Image Capture",
                CapabilityKey = "camera.capture",
                CapabilityType = "Device",
                Status = "Active"
            }
        ];

        public Task<CapabilityEntity?> GetAsync(string capabilityId, CancellationToken ct = default) =>
            Task.FromResult(_items.FirstOrDefault(x =>
                string.Equals(x.RowKey, capabilityId, StringComparison.Ordinal)));

        public Task<IReadOnlyList<CapabilityEntity>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CapabilityEntity>>(_items);

        public Task CreateAsync(CapabilityEntity e, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(CapabilityEntity e, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string capabilityId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubCommandStore : IAgentCommandStore
    {
        public AgentCommandEntity? Created { get; private set; }

        public Task CreateAsync(AgentCommandEntity entity, CancellationToken ct = default)
        {
            Created = entity;
            return Task.CompletedTask;
        }

        public Task<AgentCommandEntity?> GetAsync(string t, string s, string id, CancellationToken ct = default) =>
            Task.FromResult<AgentCommandEntity?>(null);

        public Task<IReadOnlyList<AgentCommandEntity>> GetByAgentAsync(
            string t, string s, string a, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentCommandEntity>>([]);

        public Task<IReadOnlyList<AgentCommandEntity>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentCommandEntity>>([]);

        public Task UpdateAsync(AgentCommandEntity entity, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubPublisher : IAgentCommandPublisher
    {
        public List<AgentCommandQueueMessage> Published { get; } = [];

        public Task PublishAgentCommandAsync(AgentCommandQueueMessage m, CancellationToken ct = default)
        {
            Published.Add(m);
            return Task.CompletedTask;
        }

        public Task PublishRestartCommandAsync(string a, string? c = null, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task PublishDeployCommandAsync(string a, string? v = null, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task PublishClassifyCommandAsync(ClassifyCaptureQueueMessage m, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubAgents : IAgentQueryService
    {
        public Task<AgentSummaryDto?> GetAgentAsync(TenantContext t, string agentId, CancellationToken ct = default) =>
            Task.FromResult<AgentSummaryDto?>(new AgentSummaryDto(
                agentId, "Capture Agent", "host", "1.1.9", "net10.0", "linux",
                "Online", DateTime.UtcNow, DateTime.UtcNow, TimeSpan.FromMinutes(1),
                DateTime.UtcNow, t.TenantId, t.SiteId, null,
                new ConfigurationSyncStatusDto(null, null, null, ConfigurationSyncStatus.UpToDate),
                new AgentVersionStatusDto(null, null, AgentVersionStatus.Unknown),
                "Active"));

        public Task<IReadOnlyList<AgentSummaryDto>> GetAgentsAsync(TenantContext t, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentSummaryDto>>([]);

        public Task<IReadOnlyList<AgentMetricSampleDto>> GetAgentMetricsAsync(
            TenantContext t, string a, int d, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentMetricSampleDto>>([]);
    }

    private sealed class StubDevices : IDeviceQueryService
    {
        public Task<DeviceSummaryDto?> GetDeviceAsync(TenantContext t, string deviceId, CancellationToken ct = default)
        {
            return Task.FromResult<DeviceSummaryDto?>(new DeviceSummaryDto(
                deviceId, "Kitchen Camera", "Camera", "Online", null,
                DateTime.UtcNow, null, TimeSpan.FromMinutes(1),
                RuntimeAgentId, t.TenantId, t.SiteId, null, null,
                "Pacific/Auckland", "Kitchen", "Hikvision", "DS-2CD", "1.0", null,
                false, false,
                new ConfigurationSyncStatusDto(null, null, null, ConfigurationSyncStatus.UpToDate),
                "Active"));
        }

        public Task<IReadOnlyList<DeviceSummaryDto>> GetDevicesAsync(TenantContext t, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceSummaryDto>>([]);
        public Task<IReadOnlyList<DeviceEventDto>> GetDeviceEventsAsync(TenantContext t, string d, int n, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceEventDto>>([]);
        public Task<IReadOnlyList<DeviceEventDto>> GetEventsAsync(TenantContext t, int n, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceEventDto>>([]);
        public Task<IReadOnlyList<DeviceEventDto>> GetDeviceCapturesAsync(TenantContext t, string d, int n, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceEventDto>>([]);
        public Task<IReadOnlyList<DeviceEventDto>> GetDeviceBatteryReadingsAsync(TenantContext t, string d, int n, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceEventDto>>([]);
        public Task<IReadOnlyList<CaptureDaySummaryDto>> GetDeviceCaptureDaySummariesAsync(TenantContext t, string d, int n, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CaptureDaySummaryDto>>([]);
        public Task<CapturePageDto> GetDeviceCapturesByDayAsync(TenantContext t, string d, DateOnly day, int a, int b, CancellationToken ct = default) =>
            Task.FromResult(new CapturePageDto([], false));
    }

    private sealed class StubDeviceCapabilities : IDeviceCapabilityStore
    {
        // The derived-capability branch is the only caller of this. Whether
        // it ran at all, and with which value, is how Test D observes which
        // identity ValidateAsync was handed.
        public List<string> DerivedLookups { get; } = [];

        public Task<DeviceCapabilityEntity?> GetActiveByDeviceAndCapabilityAsync(
            string t, string s, string d, string c, CancellationToken ct = default)
        {
            DerivedLookups.Add(c);
            return Task.FromResult<DeviceCapabilityEntity?>(null);
        }

        public Task<DeviceCapabilityEntity?> GetAsync(string t, string s, string id, CancellationToken ct = default) =>
            Task.FromResult<DeviceCapabilityEntity?>(null);
        public Task<IReadOnlyList<DeviceCapabilityEntity>> GetByDeviceAsync(string t, string s, string d, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceCapabilityEntity>>([]);
        public Task<IReadOnlyList<DeviceCapabilityEntity>> GetByExecutingAgentAsync(string t, string s, string a, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceCapabilityEntity>>([]);
        public Task CreateAsync(DeviceCapabilityEntity e, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(DeviceCapabilityEntity e, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubAgentRegistry : IAgentRegistryStore
    {
        public Task<AgentRegistryEntity?> GetByRuntimeAgentIdAsync(string t, string s, string r, CancellationToken ct = default) =>
            Task.FromResult<AgentRegistryEntity?>(null);
        public Task<AgentRegistryEntity?> GetAsync(string t, string s, string a, CancellationToken ct = default) =>
            Task.FromResult<AgentRegistryEntity?>(null);
        public Task<IReadOnlyList<AgentRegistryEntity>> ListAsync(string t, string s, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentRegistryEntity>>([]);
        public Task CreateAsync(AgentRegistryEntity e, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(AgentRegistryEntity e, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string t, string s, string a, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubAgentConfigurations : IAgentConfigurationStore
    {
        public Task<AgentConfigurationEntity?> GetAsync(string pk, string rk, CancellationToken ct = default) =>
            Task.FromResult<AgentConfigurationEntity?>(null);
        public Task UpsertAsync(AgentConfigurationEntity e, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(AgentConfigurationEntity e, CancellationToken ct = default) => Task.CompletedTask;
    }
}
