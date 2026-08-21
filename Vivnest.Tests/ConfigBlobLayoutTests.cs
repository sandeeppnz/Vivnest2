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
            new(RuntimeAgentId, "Capture Agent", [], []);

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
    private static ConfigBlobKey Legacy => ConfigBlobKey.Unscoped(RuntimeAgentId);

    // Rewrites an already-published entity to look as it would have before
    // tenant/site scoping existed: legacy blobs only, metadata row intact.
    //
    // Until 2026-08-21 the tests got this for free, because every publish
    // mirrored itself into the legacy layout. That dual-write is gone, so
    // the "pre-scoping entity" that the read fallbacks and the backfill
    // exist to rescue now has to be constructed deliberately - which is
    // more honest anyway: it states the situation being modelled rather
    // than relying on a side effect of the code under test.
    private static void DemoteToLegacyOnly(Harness h, int version)
    {
        var c = AgentConfigBlob.ContainerName;

        h.Blobs.Seed(c, AgentConfigBlob.VersionBlobName(Legacy, version),
            h.Blobs.Get(c, AgentConfigBlob.VersionBlobName(Scoped, version))!);
        h.Blobs.Seed(c, AgentConfigBlob.BlobName(Legacy),
            h.Blobs.Get(c, AgentConfigBlob.BlobName(Scoped))!);

        h.Blobs.Delete(c, AgentConfigBlob.VersionBlobName(Scoped, version));
        h.Blobs.Delete(c, AgentConfigBlob.ManifestBlobName(Scoped));
        h.Blobs.Delete(c, AgentConfigBlob.BlobName(Scoped));
    }

    // ---- the key itself --------------------------------------------------

    [Fact]
    public void AScopedKeyPutsTenantAndSiteInFrontOfTheName()
    {
        Assert.Equal($"{Tenant}/{Site}/", Scoped.Prefix);
        Assert.Equal($"{Tenant}/{Site}/{RuntimeAgentId}.json", AgentConfigBlob.BlobName(Scoped));
        Assert.Equal($"{Tenant}/{Site}/{RuntimeAgentId}/current.json", AgentConfigBlob.ManifestBlobName(Scoped));
        Assert.Equal($"{Tenant}/{Site}/{RuntimeAgentId}/versions/3.json", AgentConfigBlob.VersionBlobName(Scoped, 3));
    }

    // A missing tenant or site is not an error - it selects the old layout,
    // which is what an un-migrated agent and every existing blob use.
    [Theory]
    [InlineData("", "")]
    [InlineData("tenant-1", "")]
    [InlineData("", "site-1")]
    [InlineData("   ", "site-1")]
    public void AKeyMissingTenantOrSiteFallsBackToTheOldFlatNames(string tenantId, string siteId)
    {
        var key = new ConfigBlobKey(tenantId, siteId, RuntimeAgentId);

        Assert.True(key.IsUnscoped);
        Assert.Equal("", key.Prefix);
        Assert.Equal($"{RuntimeAgentId}.json", AgentConfigBlob.BlobName(key));
    }

    // The old single-argument overloads must keep producing exactly what
    // they always did - every already-published blob is named by them.
    [Fact]
    public void TheUnscopedOverloadsAreUnchanged()
    {
        Assert.Equal($"{RuntimeAgentId}.json", AgentConfigBlob.BlobName(RuntimeAgentId));
        Assert.Equal($"{RuntimeAgentId}/current.json", AgentConfigBlob.ManifestBlobName(RuntimeAgentId));
        Assert.Equal($"{RuntimeAgentId}/versions/7.json", AgentConfigBlob.VersionBlobName(RuntimeAgentId, 7));
        Assert.Equal($"{RuntimeAgentId}.json", DeviceConfigBlob.BlobName(RuntimeAgentId));
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

    // The dual-write was removed on 2026-08-21, once every deployed Agent
    // read the scoped layout - ADR-091's own exit condition. Asserted
    // rather than merely dropped: a publish that quietly started mirroring
    // again would be writing to a location nothing reads.
    [Fact]
    public async Task PublishNoLongerMirrorsIntoTheLegacyLayout()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        Assert.Null(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Legacy, 1)));
        Assert.Null(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName(Legacy)));
        Assert.Null(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.BlobName(Legacy)));
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

    // The migration case: a version published before scoping exists only at
    // the unscoped name, and rolling back to it must still work.
    [Fact]
    public async Task RollbackFindsAVersionThatOnlyExistsInTheLegacyLayout()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        // Exactly the state of every entity published before scoping.
        DemoteToLegacyOnly(h, version: 1);

        var result = await h.Publisher.RollbackAsync(TenantContext, AdminAgentId, 1);

        Assert.True(result!.Published);
        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 2)));
    }

    [Fact]
    public async Task RollingBackToAVersionInNeitherLayoutIsStillRejected()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        var result = await h.Publisher.RollbackAsync(TenantContext, AdminAgentId, 99);

        Assert.False(result!.Published);
        Assert.Contains("does not exist", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the migration gap the first real run exposed -------------------

    // An entity whose content has not changed is a version no-op - but it
    // still has to acquire scoped blobs, or it never migrates at all. This
    // was found by running the republish pass against real storage: the one
    // device that had changed migrated, and both agents (unchanged) got
    // nothing, because the no-op guard returns before any blob is written.
    [Fact]
    public async Task AnUnchangedEntityStillGetsBackfilledIntoTheScopedLayout()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        DemoteToLegacyOnly(h, version: 1);

        // Republish with identical content - a no-op by hash.
        var result = await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        Assert.False(result!.Published);
        Assert.Contains("unchanged", result.Reason, StringComparison.OrdinalIgnoreCase);

        // ...and yet the scoped layout now exists, at the SAME version.
        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 1)));
        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName(Scoped)));
        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.BlobName(Scoped)));
        Assert.Null(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 2)));
    }

    // The backfilled manifest must point into the SCOPED layout. Copying
    // the legacy manifest verbatim would leave it pointing back at the
    // legacy version blob, so deleting those later would break exactly the
    // entities the backfill was meant to rescue.
    [Fact]
    public async Task TheBackfilledManifestPointsAtTheScopedVersionNotTheLegacyOne()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);
        DemoteToLegacyOnly(h, version: 1);

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        var manifest = JsonSerializer.Deserialize<ConfigurationManifest>(
            h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName(Scoped))!)!;

        Assert.Equal(AgentConfigBlob.VersionBlobName(Scoped, 1), manifest.ConfigurationUri);
        Assert.Equal(1, manifest.ConfigurationVersion);
    }

    // Once migrated, a further no-op must not rewrite anything - otherwise
    // every republish would churn blobs for no reason.
    [Fact]
    public async Task BackfillIsSkippedOnceTheScopedLayoutExists()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);
        var uploadsAfterFirstPublish = h.Blobs.Uploads.Count;

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        Assert.Equal(uploadsAfterFirstPublish, h.Blobs.Uploads.Count);
    }

    // The Agent merge-patches its flat blob to preserve agent-local
    // sections. On the first scoped publish there is no scoped blob to
    // merge onto, so it has to fall back to the legacy one or a Low-type
    // agent would lose its HomeAssistant section.
    [Fact]
    public async Task TheFirstScopedPublishStillPreservesAgentLocalSections()
    {
        var h = new Harness();

        h.Blobs.Seed(
            AgentConfigBlob.ContainerName,
            AgentConfigBlob.BlobName(Legacy),
            System.Text.Encoding.UTF8.GetBytes(
                """{"HomeAssistant":{"BaseUrl":"http://ha.local","Enabled":true}}"""));

        await h.Publisher.PublishAsync(TenantContext, AdminAgentId);

        var scoped = System.Text.Encoding.UTF8.GetString(
            h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.BlobName(Scoped))!);

        Assert.Contains("HomeAssistant", scoped, StringComparison.Ordinal);
        Assert.Contains("http://ha.local", scoped, StringComparison.Ordinal);
    }
}
