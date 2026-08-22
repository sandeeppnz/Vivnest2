using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Functions.Http;
using Vivnest.Cloud.Options;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Tests;

// Drives the real AgentCommandsFunction through its real auth gate, with
// AgentAuth:RequireApiKey both off (today's deployed setting) and on (what
// flipping the flag would do). No Azure involved - the Function's four
// dependencies are all interfaces, and HttpRequest comes from a
// DefaultHttpContext.
//
// The question these answer is narrow and specific: does turning the flag
// on actually close the hole, and does it close it without also breaking
// the callers that are supposed to keep working?
public class AgentCommandAuthTests
{
    private const string AgentId = "agent-1";
    private const string OtherAgentId = "agent-2";
    private const string CommandId = "cmd-1";

    // The tenant/site the agent's own key is bound to.
    private const string KeyTenant = "tenant-real";
    private const string KeySite = "site-real";

    // What an unauthenticated caller claims in the query string / body -
    // deliberately different, so it is visible which one the gate used.
    private const string ClaimedTenant = "tenant-claimed";
    private const string ClaimedSite = "site-claimed";

    private sealed class StubAuthenticator : IApiKeyAuthenticator
    {
        public TenantContext? Result { get; set; }

        public Task<TenantContext?> AuthenticateAsync(
            string? apiKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.IsNullOrEmpty(apiKey) ? null : Result);
    }

    private sealed class StubCommands : IAgentCommandManagementService
    {
        public List<(string TenantId, string SiteId)> Reads { get; } = [];

        private static AgentCommandDto Command(string targetAgentId, string tenantId, string siteId) =>
            new(CommandId, targetAgentId, null, null, "RestartAgent", "Dispatched", null, null, null, null,
                "test", DateTime.UtcNow, null, null, null, null, DateTime.UtcNow.AddMinutes(5), tenantId, siteId);

        public Task<IReadOnlyList<AgentCommandDto>> GetByAgentAsync(
            string tenantId, string siteId, string agentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentCommandDto>>([Command(agentId, tenantId, siteId)]);

        public Task<AgentCommandDto?> GetAsync(
            string tenantId, string siteId, string commandId, CancellationToken cancellationToken = default)
        {
            Reads.Add((tenantId, siteId));
            return Task.FromResult<AgentCommandDto?>(Command(AgentId, tenantId, siteId));
        }

        public Task<AgentCommandDto?> UpdateStatusAsync(
            string tenantId, string siteId, string commandId, AgentCommandStatus status,
            string? result, string? errorCode, string? errorMessage,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((tenantId, siteId));
            return Task.FromResult<AgentCommandDto?>(Command(AgentId, tenantId, siteId));
        }

        public Task<int> ExpireStaleAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task EvaluateAgentCommandsAsync(
            AgentHeartbeatEntity agent, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class Harness
    {
        // ADR-102 - GetCommand translates CapabilityId into the runtime
        // CapabilityKey. These tests are about auth, so the store is empty
        // and every command forwards its CapabilityId unchanged, which is
        // the documented pass-through for an unresolvable id.
        public sealed class StubCapabilities : ICapabilityStore
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

        public StubAuthenticator Auth { get; } = new();
        public StubCommands Commands { get; } = new();
        public StubCapabilities Capabilities { get; } = new();
        public AgentCommandsFunction Function { get; }

        public Harness(bool requireApiKey)
        {
            Function = new AgentCommandsFunction(
                Auth,
                Commands,
                Capabilities,
                Options.Create(new AgentAuthOptions { RequireApiKey = requireApiKey }),
                NullLogger<AgentCommandsFunction>.Instance);
        }
    }

    private static HttpRequest Request(string? apiKey, bool withClaimedScope = true)
    {
        var context = new DefaultHttpContext();
        var request = context.Request;

        if (apiKey != null)
            request.Headers["x-api-key"] = apiKey;

        if (withClaimedScope)
        {
            request.QueryString = new QueryString(
                $"?tenantId={ClaimedTenant}&siteId={ClaimedSite}");
        }

        return request;
    }

    private static HttpRequest StatusRequest(string? apiKey)
    {
        var request = Request(apiKey, withClaimedScope: false);
        var body = JsonSerializer.Serialize(new
        {
            TenantId = ClaimedTenant,
            SiteId = ClaimedSite,
            Status = "Succeeded",
            Result = (string?)null,
            ErrorCode = (string?)null,
            ErrorMessage = (string?)null
        });

        request.ContentType = "application/json";
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return request;
    }

    // ---- flag OFF: today's deployed behaviour ----------------------------

    // The hole itself. This is what is live right now.
    [Fact]
    public async Task WithTheFlagOff_AnUnauthenticatedCallerIsHonoured()
    {
        var h = new Harness(requireApiKey: false);

        var result = await h.Function.GetCommand(Request(apiKey: null), AgentId, CommandId, default);

        Assert.IsType<OkObjectResult>(result);

        // ...and on the tenant/site the *caller* supplied, unverified.
        Assert.Equal((ClaimedTenant, ClaimedSite), h.Commands.Reads.Single());
    }

    // ResolveAgentScope's own comment claims "a *tenant* key is never
    // accepted here even in grace mode." That overstates it. What actually
    // happens is that AuthenticateAgentAsync returns null for a tenant key,
    // so the call falls through to the same grace-mode branch an anonymous
    // call takes and is honoured on the CALLER-SUPPLIED ids.
    //
    // Not an escalation - anonymous already passes in grace mode, so the
    // key confers nothing, and its own tenant/site are ignored. But
    // "rejected" and "honoured, with your key ignored" are different
    // things, and the comment says the first. Asserting the real behaviour.
    [Fact]
    public async Task WithTheFlagOff_ATenantKeyIsHonouredAsIfAnonymous()
    {
        var h = new Harness(requireApiKey: false);
        h.Auth.Result = new TenantContext(KeyTenant, KeySite, DevicesOnly: false);

        var result = await h.Function.GetCommand(Request("tenant-key"), AgentId, CommandId, default);

        Assert.IsType<OkObjectResult>(result);

        // The key's own tenant/site were not used - the claimed ones were.
        Assert.Equal((ClaimedTenant, ClaimedSite), h.Commands.Reads.Single());
    }

    // ...and once the flag is on, the tenant key IS rejected, which is what
    // the comment describes. So the overstatement is only about grace mode.
    [Fact]
    public async Task WithTheFlagOn_ATenantKeyIsRejected()
    {
        var h = new Harness(requireApiKey: true);
        h.Auth.Result = new TenantContext(KeyTenant, KeySite, DevicesOnly: false);

        var result = await h.Function.GetCommand(Request("tenant-key"), AgentId, CommandId, default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(h.Commands.Reads);
    }

    // ---- flag ON: what flipping it would do ------------------------------

    [Fact]
    public async Task WithTheFlagOn_AnUnauthenticatedCallerIsRejected()
    {
        var h = new Harness(requireApiKey: true);

        var result = await h.Function.GetCommand(Request(apiKey: null), AgentId, CommandId, default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(h.Commands.Reads);
    }

    [Fact]
    public async Task WithTheFlagOn_TheStatusCallbackIsRejectedTooNotJustTheRead()
    {
        var h = new Harness(requireApiKey: true);

        var result = await h.Function.UpdateCommandStatus(
            StatusRequest(apiKey: null), AgentId, CommandId, default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(h.Commands.Reads);
    }

    // The migration's whole point: an agent that HAS a key keeps working.
    [Fact]
    public async Task WithTheFlagOn_AnAgentWithItsOwnKeyStillWorks()
    {
        var h = new Harness(requireApiKey: true);
        h.Auth.Result = new TenantContext(KeyTenant, KeySite, DevicesOnly: false, AgentId: AgentId);

        var result = await h.Function.GetCommand(Request("agent-key"), AgentId, CommandId, default);

        Assert.IsType<OkObjectResult>(result);
    }

    // And it is scoped by the KEY's tenant/site, not by whatever the
    // caller put in the query string - which is the actual fix, not just
    // the rejection above.
    [Fact]
    public async Task AnAgentKeysOwnTenantWinsOverTheCallerSuppliedOne()
    {
        var h = new Harness(requireApiKey: true);
        h.Auth.Result = new TenantContext(KeyTenant, KeySite, DevicesOnly: false, AgentId: AgentId);

        await h.Function.GetCommand(Request("agent-key"), AgentId, CommandId, default);

        Assert.Equal((KeyTenant, KeySite), h.Commands.Reads.Single());
    }

    // A key for one agent must not reach another agent's commands, flag or
    // no flag.
    [Fact]
    public async Task AnAgentKeyCannotActOnADifferentAgent()
    {
        foreach (var requireApiKey in new[] { true, false })
        {
            var h = new Harness(requireApiKey);
            h.Auth.Result = new TenantContext(KeyTenant, KeySite, DevicesOnly: false, AgentId: OtherAgentId);

            var result = await h.Function.GetCommand(Request("agent-key"), AgentId, CommandId, default);

            Assert.IsType<UnauthorizedResult>(result);
            Assert.Empty(h.Commands.Reads);
        }
    }

    // The dashboard-facing route on the same class must keep taking a
    // tenant key and must NOT start accepting agent keys.
    [Fact]
    public async Task TheDashboardRouteIsUnaffectedByTheFlag()
    {
        var withTenantKey = new Harness(requireApiKey: true);
        withTenantKey.Auth.Result = new TenantContext(KeyTenant, KeySite, DevicesOnly: false);

        Assert.IsType<OkObjectResult>(
            await withTenantKey.Function.GetAgentCommands(Request("tenant-key"), AgentId, default));

        var withAgentKey = new Harness(requireApiKey: true);
        withAgentKey.Auth.Result =
            new TenantContext(KeyTenant, KeySite, DevicesOnly: false, AgentId: AgentId);

        Assert.IsType<UnauthorizedResult>(
            await withAgentKey.Function.GetAgentCommands(Request("agent-key"), AgentId, default));
    }
}
