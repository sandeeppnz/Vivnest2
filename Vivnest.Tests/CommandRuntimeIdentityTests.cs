using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Functions.Http;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Cloud.Options;

namespace Vivnest.Tests;

// ADR-102. The identity bridge, from the Agent's side.
//
// A command row stores CapabilityId as the catalogue GUID, because the row
// is admin history and has to stay joinable to the catalogue entry it came
// from. The Agent has never heard of that GUID - it knows camera.capture -
// so GetCommand translates on the way out.
//
// GetCommand is Agent-facing only (ADR-079: GetAgentCommands is the
// dashboard's route), which is why the translation lives on the route
// rather than in the shared CommandDispatcher.ToDto. The dashboard still
// sees the catalogue id.
public class CommandRuntimeIdentityTests
{
    private const string CatalogueGuid = "5217f0ef-f7c6-4d9f-9723-7bf2afad5572";
    private const string RuntimeKey = "camera.capture";
    private const string AgentId = "agent-runtime-1";

    private static CapabilityEntity Capability(string? key) =>
        new()
        {
            PartitionKey = "capability",
            RowKey = CatalogueGuid,
            CapabilityName = "Image Capture",
            CapabilityKey = key!,
            CapabilityType = "Device",
            Status = "Active"
        };

    private static AgentCommandDto Command(string? capabilityId) =>
        new(
            "command-1", AgentId, "device-1", capabilityId,
            "ExecuteCapability", "Dispatched",
            null, null, null, null,
            "test",
            DateTime.UtcNow, DateTime.UtcNow, null, null, null,
            DateTime.UtcNow.AddMinutes(5),
            "tenant-1", "site-1");

    private static async Task<AgentCommandDto?> FetchAsync(
        AgentCommandDto stored, CapabilityEntity? capability)
    {
        var capabilities = new StubCapabilities();

        if (capability != null)
            capabilities.Items[capability.RowKey] = capability;

        var function = new AgentCommandsFunction(
            new StubAuthenticator(),
            new StubCommands { Stored = stored },
            capabilities,
            Options.Create(new AgentAuthOptions { RequireApiKey = false }),
            NullLogger<AgentCommandsFunction>.Instance);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenantId=tenant-1&siteId=site-1");

        var result = await function.GetCommand(
            context.Request, AgentId, stored.CommandId, CancellationToken.None);

        return (result as OkObjectResult)?.Value as AgentCommandDto;
    }

    // The bridge: catalogue GUID in storage, runtime key on the wire.
    [Fact]
    public async Task TheCatalogueIdIsTranslatedIntoTheRuntimeKey()
    {
        var fetched = await FetchAsync(Command(CatalogueGuid), Capability(RuntimeKey));

        Assert.NotNull(fetched);
        Assert.Equal(RuntimeKey, fetched!.CapabilityId);
    }

    // Unresolvable ids are forwarded unchanged rather than blanked: a
    // command the Agent cannot route beats one it cannot even name in the
    // failure it reports.
    [Fact]
    public async Task AnUnknownCapabilityIdIsForwardedUnchanged()
    {
        var fetched = await FetchAsync(Command("not-in-the-catalogue"), capability: null);

        Assert.Equal("not-in-the-catalogue", fetched!.CapabilityId);
    }

    // A catalogue row with no key yet - written before ADR-096 - must not
    // translate to empty.
    [Fact]
    public async Task ACapabilityWithNoKeyIsForwardedUnchanged()
    {
        var fetched = await FetchAsync(Command(CatalogueGuid), Capability(key: null));

        Assert.Equal(CatalogueGuid, fetched!.CapabilityId);
    }

    // RefreshConfiguration, ApplyConfiguration and RestartAgent name no
    // capability at all.
    [Fact]
    public async Task ACommandWithNoCapabilityIsUntouched()
    {
        var fetched = await FetchAsync(Command(capabilityId: null), capability: null);

        Assert.Null(fetched!.CapabilityId);
    }

    private sealed class StubAuthenticator : IApiKeyAuthenticator
    {
        // No key presented: GetCommand falls back to the tenantId/siteId
        // query parameters, which is the Agent's own trust model (ADR-079).
        public Task<TenantContext?> AuthenticateAsync(string? apiKey, CancellationToken ct = default) =>
            Task.FromResult<TenantContext?>(null);
    }

    private sealed class StubCommands : IAgentCommandManagementService
    {
        public AgentCommandDto? Stored { get; set; }

        public Task<AgentCommandDto?> GetAsync(
            string tenantId, string siteId, string commandId, CancellationToken ct = default) =>
            Task.FromResult(Stored);

        public Task<IReadOnlyList<AgentCommandDto>> GetByAgentAsync(
            string tenantId, string siteId, string agentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentCommandDto>>([]);

        public Task<AgentCommandDto?> UpdateStatusAsync(
            string tenantId, string siteId, string commandId, AgentCommandStatus status,
            string? result, string? errorCode, string? errorMessage, CancellationToken ct = default) =>
            Task.FromResult<AgentCommandDto?>(null);

        public Task EvaluateAgentCommandsAsync(
            AgentHeartbeatEntity agent, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubCapabilities : ICapabilityStore
    {
        public Dictionary<string, CapabilityEntity> Items { get; } = new(StringComparer.Ordinal);

        public Task<CapabilityEntity?> GetAsync(string capabilityId, CancellationToken ct = default) =>
            Task.FromResult(Items.TryGetValue(capabilityId, out var e) ? e : null);

        public Task<IReadOnlyList<CapabilityEntity>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CapabilityEntity>>(Items.Values.ToList());

        public Task CreateAsync(CapabilityEntity entity, CancellationToken ct = default)
        {
            Items[entity.RowKey] = entity;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(CapabilityEntity entity, CancellationToken ct = default)
        {
            Items[entity.RowKey] = entity;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string capabilityId, CancellationToken ct = default)
        {
            Items.Remove(capabilityId);
            return Task.CompletedTask;
        }
    }
}
