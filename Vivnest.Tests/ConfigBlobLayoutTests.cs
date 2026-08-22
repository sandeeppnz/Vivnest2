using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;

namespace Vivnest.Tests;


// Tenant/site scoping of the configuration blob layout, and the dual-write
// that lets a half-migrated estate keep working.
//
// The point of the scoping is that an Agent enumerates and downloads only
// its own prefix instead of the whole container - it used to pull every
// other tenant's device names, locations, brands and RTSP URLs across the
// wire and discard them after an ownership check. It is NOT a complete
// tenant boundary on its own: Agents still hold an account-level storage
// connection string, so nothing here stops a determined one reading
// another prefix. That is a separate finding.
public class ConfigBlobLayoutTests
{
    private const string RuntimeAgentId = "agent-runtime-1";
    private const string AdminAgentId = "agent-admin-1";
    private const string Tenant = "tenant-1";
    private const string Site = "site-1";

    private static readonly TenantContext TenantContext = new(Tenant, Site, DevicesOnly: false);

    private sealed class StubProjector : IAgentRuntimeConfigurationProjector
    {
        public AgentRuntimeConfigurationDocumentDto Document { get; set; } =
            // Devices, Capabilities, Warnings - Capabilities was added
            // between Devices and Warnings when AgentCapability assignments
            // started reaching the wire (ADR-096).
            new(RuntimeAgentId, "Capture Agent", [], [], []);

        public Task<AgentRuntimeConfigurationDocumentDto?> ProjectAsync(
            TenantContext tenant, string agentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentRuntimeConfigurationDocumentDto?>(Document);
    }

    private sealed class StubDispatcher : ICommandDispatcher
    {
        public Task<AgentCommandDto?> DispatchAsync(
            TenantContext tenant, string commandType, string targetAgentId, string requestedBy,
            string? targetDeviceId = null, string? capabilityId = null, string? payload = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentCommandDto?>(null);
    }

    private sealed class Harness
    {
        public StubProjector Projector { get; } = new();
        public FakeBlobStorageClient Blobs { get; } = new();
        public FakeAgentConfigurationStore Configurations { get; } = new();
        public FakeAgentEventStore Events { get; } = new();
        public AgentRuntimeConfigurationPublisher Publisher { get; }

        public Harness()
        {
            var writer = new RuntimeConfigurationWriter<AgentConfigurationEntity>(
                Blobs,
                Configurations,
                new StubDispatcher(),
                Options.Create(new CredentialEncryptionOptions
                {
                    Key = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
                }),
                NullLogger<RuntimeConfigurationWriter<AgentConfigurationEntity>>.Instance);

            Publisher = new AgentRuntimeConfigurationPublisher(Projector, Blobs, Events, writer);
        }
    }

    private static ConfigBlobKey Scoped => new(Tenant, Site, RuntimeAgentId);

    // ---- the key itself --------------------------------------------------

    [Fact]
    public void AScopedKeyPutsTenantAndSiteInFrontOfTheName()
    {
        Assert.Equal($"{Tenant}/{Site}/", Scoped.Prefix);
        Assert.Equal($"{Tenant}/{Site}/{RuntimeAgentId}.json", AgentConfigBlob.BlobName(Scoped));
        Assert.Equal($"{Tenant}/{Site}/{RuntimeAgentId}/current.json", AgentConfigBlob.ManifestBlobName(Scoped));
        Assert.Equal($"{Tenant}/{Site}/{RuntimeAgentId}/versions/3.json", AgentConfigBlob.VersionBlobName(Scoped, 3));
    }

    // A missing tenant or site used to be meaningful: it selected the flat
    // layout. With that layout deleted (ADR-091) the same input would
    // address a blob that cannot exist, so an Agent misconfigured this way
    // would report "no configuration" rather than "you have not told me
    // which tenant I belong to". Rejecting it makes the cause visible at
    // the point it goes wrong.
    [Theory]
    [InlineData("", "")]
    [InlineData("tenant-1", "")]
    [InlineData("", "site-1")]
    [InlineData("   ", "site-1")]
    public void AKeyMissingTenantOrSiteIsRejected(string tenantId, string siteId)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new ConfigBlobKey(tenantId, siteId, RuntimeAgentId));

        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("site", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- what a publish actually writes ----------------------------------

    [Fact]
    public async Task PublishWritesTheScopedVersionManifestAndFlatBlobs()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 1)));
        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName(Scoped)));
        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.BlobName(Scoped)));
    }

    // Each manifest has to point into its OWN layout, or a reader that
    // found the legacy manifest would be sent to a scoped version blob it
    // may not be looking for - and vice versa.
    [Fact]
    public async Task EachManifestPointsIntoItsOwnLayout()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        var scoped = JsonSerializer.Deserialize<ConfigurationManifest>(
            h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName(Scoped))!)!;

        // ConfigurationUri is a full container-relative name, so a manifest
        // must point inside its own layout. Only the scoped one is written
        // now, but the property still matters: the backfill rebuilds this
        // field rather than copying it, for exactly this reason.
        Assert.Equal(AgentConfigBlob.VersionBlobName(Scoped, 1), scoped.ConfigurationUri);
        Assert.Equal(1, scoped.ConfigurationVersion);
    }

    // Scoping must not disturb version numbering: the counter lives on the
    // metadata row, not in the blob names.
    [Fact]
    public async Task VersionNumberingIsUnaffectedByTheLayout()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);
        h.Projector.Document = h.Projector.Document with { Name = "Second" };
        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 2)));
        Assert.Null(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 3)));
    }

    // ---- reading back ----------------------------------------------------

    // Rollback re-publishes an old version's content as a NEW version
    // rather than rewinding the counter, so version blobs stay immutable.
    //
    // This used to be the migration case - reaching a version that existed
    // only under the unscoped name. Versions published before scoping were
    // copied into the scoped layout before those blobs were deleted
    // (ADR-091), so there is only one place left to look.
    [Fact]
    public async Task RollbackRepublishesAnEarlierVersionAsANewOne()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        var result = await h.Publisher.RollbackAsync(TenantContext, AdminAgentId, 1);

        Assert.True(result!.Published);
        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 2)));
    }

    [Fact]
    public async Task RollingBackToAVersionThatDoesNotExistIsRejected()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        var result = await h.Publisher.RollbackAsync(TenantContext, AdminAgentId, 99);

        Assert.False(result!.Published);
        Assert.Contains("does not exist", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- no-op and merge behaviour --------------------------------------

    // Republishing identical content must write nothing at all - not a new
    // version, and not a rewrite of the existing blobs. The no-op guard
    // used to have a second job (driving the scoped-layout backfill, which
    // deliberately DID write on this path); with the backfill gone this is
    // the whole of its behaviour again.
    [Fact]
    public async Task ANoOpRepublishWritesNothing()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);
        var uploadsAfterFirstPublish = h.Blobs.Uploads.Count;

        var result = await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        Assert.False(result!.Published);
        Assert.Contains("unchanged", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(uploadsAfterFirstPublish, h.Blobs.Uploads.Count);
    }

    // The Agent publisher merge-patches its flat blob rather than replacing
    // it, so sections only the Agent knows about (a Low-type agent's
    // HomeAssistant) survive a publish from the dashboard, which knows
    // nothing about them.
    [Fact]
    public async Task APublishPreservesAgentLocalSectionsOnTheFlatBlob()
    {
        var h = new Harness();

        h.Blobs.Seed(
            AgentConfigBlob.ContainerName,
            AgentConfigBlob.BlobName(Scoped),
            System.Text.Encoding.UTF8.GetBytes(
                """{"HomeAssistant":{"BaseUrl":"http://ha.local","Enabled":true}}"""));

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        var flat = System.Text.Encoding.UTF8.GetString(
            h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.BlobName(Scoped))!);

        Assert.Contains("HomeAssistant", flat, StringComparison.Ordinal);
        Assert.Contains("http://ha.local", flat, StringComparison.Ordinal);
    }
}
