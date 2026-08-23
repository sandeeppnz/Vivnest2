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
    private const string RegistryAgentId = "agent-registry-1";
    private const string DeviceId = "device-1";

    // ADR-104. The whole point is that these are DIFFERENT values: the
    // command names the runtime device, the assignment is keyed by the
    // registry device, and a test that used one id for both would pass
    // whether or not anything translated.
    private const string RuntimeDeviceId = "device-1";
    private const string RegistryDeviceId = "registry-device-1";

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
    //
    // The device mapping is now a precondition of reaching that branch at
    // all (ADR-104): without it validation stops at DEVICE_NOT_REGISTERED
    // and never asks about the capability. Set up explicitly rather than
    // by relaxing the assertion - the branch this test exists to observe
    // is downstream of the device boundary now.
    [Fact]
    public async Task ACatalogueIdTakesTheDerivedValidationBranch()
    {
        var h = new Harness();
        h.DeviceRegistry.Map(RuntimeDeviceId, RegistryDeviceId);

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
    // ADR-104 - the device identity boundary. Symmetric with ADR-081's
    // agent boundary, which lives in the same validation branch.
    // =====================================================================

    // ---- Test A: the registry is asked using the RUNTIME id --------------
    // The first half of the translation. If someone "simplifies" this by
    // passing an already-registry id, this fails.
    [Fact]
    public async Task TheDeviceRegistryIsQueriedWithTheRuntimeDeviceId()
    {
        var h = new Harness();
        h.DeviceRegistry.Map(RuntimeDeviceId, RegistryDeviceId);

        await h.DispatchExecuteCapabilityAsync(CatalogueGuid);

        Assert.Equal([RuntimeDeviceId], h.DeviceRegistry.RuntimeLookups);
    }

    // ---- Test B: the capability store is asked using the REGISTRY id -----
    // The most important test in this file. This is the bug: a runtime id
    // was passed straight into a registry-keyed column, so the lookup could
    // never match for any device or any capability.
    [Fact]
    public async Task TheCapabilityLookupUsesTheRegistryDeviceIdNotTheRuntimeOne()
    {
        var h = new Harness();
        h.DeviceRegistry.Map(RuntimeDeviceId, RegistryDeviceId);

        await h.DispatchExecuteCapabilityAsync(CatalogueGuid);

        Assert.Equal([RegistryDeviceId], h.DeviceCapabilities.DeviceLookups);
        Assert.DoesNotContain(RuntimeDeviceId, h.DeviceCapabilities.DeviceLookups);
    }

    // ---- Test C: a running device with no registry mapping ---------------
    // DEVICE_NOT_REGISTERED, and the assignment store must not be consulted
    // at all - there is no id to consult it with.
    [Fact]
    public async Task AnUnmappedRuntimeDeviceIsRejectedWithoutTouchingTheAssignmentStore()
    {
        var h = new Harness();

        var result = await h.DispatchExecuteCapabilityAsync(CatalogueGuid);

        Assert.Equal("DEVICE_NOT_REGISTERED", result!.ErrorCode);
        Assert.Empty(h.DeviceCapabilities.DeviceLookups);
    }

    // ---- Test D: mapped, but nothing assigned ----------------------------
    // The distinction that was impossible to make before: "not in the admin
    // registry" is now a different error from "registered, but this
    // capability was never assigned to it".
    [Fact]
    public async Task AMappedDeviceWithNoAssignmentIsRejectedAsNotAssigned()
    {
        var h = new Harness();
        h.DeviceRegistry.Map(RuntimeDeviceId, RegistryDeviceId);

        var result = await h.DispatchExecuteCapabilityAsync(CatalogueGuid);

        Assert.Equal("CAPABILITY_NOT_ASSIGNED", result!.ErrorCode);
        Assert.NotEqual("DEVICE_NOT_REGISTERED", result.ErrorCode);

        // It got far enough to actually ask - which is what separates this
        // from Test C.
        Assert.NotEmpty(h.DeviceCapabilities.DeviceLookups);
    }

    // ---- Test E: both boundaries translate, independently ----------------
    // ADR-081's agent translation and ADR-104's device translation are
    // separate crossings in the same branch. Asserting them together is
    // what makes the symmetry explicit - and catches a "fix" that routes
    // both through one lookup.
    [Fact]
    public async Task BothIdentityBoundariesTranslateIndependently()
    {
        var h = new Harness();
        h.DeviceRegistry.Map(RuntimeDeviceId, RegistryDeviceId);
        h.DeviceCapabilities.Assignment = new DeviceCapabilityEntity
        {
            TenantId = Tenant.TenantId,
            SiteId = Tenant.SiteId,
            DeviceId = RegistryDeviceId,
            CapabilityId = CatalogueGuid,
            ExecutingAgentId = RegistryAgentId,
            Status = "Active"
        };
        h.AgentRegistry.Map(RuntimeAgentId, RegistryAgentId);

        var result = await h.DispatchExecuteCapabilityAsync(CatalogueGuid);

        Assert.Null(result!.ErrorCode);

        // device: runtime in, registry out
        Assert.Equal([RuntimeDeviceId], h.DeviceRegistry.RuntimeLookups);
        Assert.Equal([RegistryDeviceId], h.DeviceCapabilities.DeviceLookups);

        // agent: runtime in, registry compared (ADR-081, unchanged)
        Assert.Equal([RuntimeAgentId], h.AgentRegistry.RuntimeLookups);
    }

    // A correctly-mapped device whose capability executes on a DIFFERENT
    // agent must still be rejected - proving the ADR-104 translation did
    // not weaken ADR-081's check on its way past.
    [Fact]
    public async Task TheAgentOwnershipCheckStillRejectsAForeignExecutingAgent()
    {
        var h = new Harness();
        h.DeviceRegistry.Map(RuntimeDeviceId, RegistryDeviceId);
        h.DeviceCapabilities.Assignment = new DeviceCapabilityEntity
        {
            TenantId = Tenant.TenantId,
            SiteId = Tenant.SiteId,
            DeviceId = RegistryDeviceId,
            CapabilityId = CatalogueGuid,
            ExecutingAgentId = "some-other-agent",
            Status = "Active"
        };
        h.AgentRegistry.Map(RuntimeAgentId, RegistryAgentId);

        var result = await h.DispatchExecuteCapabilityAsync(CatalogueGuid);

        Assert.Equal("WRONG_EXECUTING_AGENT", result!.ErrorCode);
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
        public StubDeviceRegistry DeviceRegistry { get; } = new();
        public StubAgentRegistry AgentRegistry { get; } = new();
        public CommandDispatcher Dispatcher { get; }

        public Harness()
        {
            Dispatcher = new CommandDispatcher(
                Commands,
                Publisher,
                new StubAgents(),
                Devices,
                DeviceCapabilities,
                AgentRegistry,
                DeviceRegistry,
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

        // ADR-104 - which DEVICE id the lookup was handed. DerivedLookups
        // records the capability id and cannot see this crossing at all.
        public List<string> DeviceLookups { get; } = [];

        public DeviceCapabilityEntity? Assignment { get; set; }

        public Task<DeviceCapabilityEntity?> GetActiveByDeviceAndCapabilityAsync(
            string t, string s, string d, string c, CancellationToken ct = default)
        {
            DerivedLookups.Add(c);
            DeviceLookups.Add(d);

            // Returned only when the caller asked with the id the
            // assignment is actually keyed by - a real store would.
            return Task.FromResult(
                Assignment != null && string.Equals(Assignment.DeviceId, d, StringComparison.Ordinal)
                    ? Assignment
                    : null);
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

    // ADR-104. Deliberately returns null unless a mapping was set up, so
    // "no registry record for this running device" is the DEFAULT state a
    // test has to opt out of - that is the condition the live system was
    // in, and it should not be the easy one to forget.
    private sealed class StubDeviceRegistry : IDeviceRegistryStore
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

        public List<string> RuntimeLookups { get; } = [];

        public void Map(string runtimeDeviceId, string registryDeviceId) =>
            _map[runtimeDeviceId] = registryDeviceId;

        public Task<DeviceRegistryEntity?> GetByRuntimeDeviceIdAsync(
            string t, string s, string runtimeDeviceId, CancellationToken ct = default)
        {
            RuntimeLookups.Add(runtimeDeviceId);

            return Task.FromResult(_map.TryGetValue(runtimeDeviceId, out var registryId)
                ? new DeviceRegistryEntity
                {
                    TenantId = t, SiteId = s,
                    PartitionKey = $"{t}|{s}",
                    RowKey = registryId,
                    RuntimeDeviceId = runtimeDeviceId
                }
                : null);
        }

        public Task<DeviceRegistryEntity?> GetAsync(string t, string s, string d, CancellationToken ct = default) =>
            Task.FromResult<DeviceRegistryEntity?>(null);
        public Task<IReadOnlyList<DeviceRegistryEntity>> ListAsync(string t, string s, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceRegistryEntity>>([]);
        public Task CreateAsync(DeviceRegistryEntity e, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(DeviceRegistryEntity e, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string t, string s, string d, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubAgentRegistry : IAgentRegistryStore
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

        public List<string> RuntimeLookups { get; } = [];

        public void Map(string runtimeAgentId, string registryAgentId) =>
            _map[runtimeAgentId] = registryAgentId;

        public Task<AgentRegistryEntity?> GetByRuntimeAgentIdAsync(string t, string s, string r, CancellationToken ct = default)
        {
            RuntimeLookups.Add(r);

            return Task.FromResult(_map.TryGetValue(r, out var registryId)
                ? new AgentRegistryEntity
                {
                    TenantId = t, SiteId = s,
                    PartitionKey = $"{t}|{s}", RowKey = registryId, RuntimeAgentId = r
                }
                : null);
        }
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
