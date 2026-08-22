//using System.Text.Json;
//using Microsoft.Extensions.Logging.Abstractions;
//using Microsoft.Extensions.Options;
//using Vivnest.Cloud.Admin;
//using Vivnest.Cloud.Admin.Interfaces;
//using Vivnest.Cloud.Api.Dtos;
//using Vivnest.Cloud.Auth;
//using Vivnest.Core.Constants;
//using Vivnest.Core.DataStores.Entities;
//using Vivnest.Core.Options;

//namespace Vivnest.Tests;

// Note to Claude - fix it, currently commented out


//// The publish pipeline is the most intricate logic in this codebase -
//// monotonic versioning, immutable version blobs, a manifest pointer, a
//// content-hash no-op guard, an ETag-guarded metadata row with a bounded
//// retry, and rollback-as-a-new-version. Until IBlobStorageClient /
//// IAgentConfigurationStore / IAgentEventStore existed it could not be
//// tested at all: every storage dependency was a concrete type, and
//// AzureTableStore<T>'s constructor reaches the network, so merely building
//// a publisher hit Azure.
//public class AgentConfigurationPublisherTests
//{
//    private const string RuntimeAgentId = "agent-runtime-1";
//    private const string AdminAgentId = "agent-admin-1";
//    private static readonly TenantContext Tenant = new("tenant-1", "site-1", DevicesOnly: false);

//    // Publishes are scoped by tenant/site (ADR-091). Since the legacy
//    // mirror was removed on 2026-08-21, these assertions have to name the
//    // scoped blobs - previously they passed against the unscoped names
//    // only because every publish wrote both.
//    private static ConfigBlobKey Scoped =>
//        new(Tenant.TenantId, Tenant.SiteId, RuntimeAgentId);

//    private sealed class StubProjector : IAgentRuntimeConfigurationProjector
//    {
//        public AgentRuntimeConfigurationDocumentDto Document { get; set; } =
//            new(RuntimeAgentId, "Capture Agent", [], []);

//        public Task<AgentRuntimeConfigurationDocumentDto?> ProjectAsync(
//            TenantContext tenant, string agentId, CancellationToken cancellationToken = default) =>
//            Task.FromResult<AgentRuntimeConfigurationDocumentDto?>(Document);
//    }

//    private sealed class StubDispatcher : ICommandDispatcher
//    {
//        public int Dispatches { get; private set; }

//        public Task<AgentCommandDto?> DispatchAsync(
//            TenantContext tenant, string commandType, string targetAgentId, string requestedBy,
//            string? targetDeviceId = null, string? capabilityId = null, string? payload = null,
//            CancellationToken cancellationToken = default)
//        {
//            Dispatches++;
//            return Task.FromResult<AgentCommandDto?>(null);
//        }
//    }

//    private sealed class Harness
//    {
//        public StubProjector Projector { get; } = new();
//        public FakeBlobStorageClient Blobs { get; } = new();
//        public FakeAgentConfigurationStore Configurations { get; } = new();
//        public FakeAgentEventStore Events { get; } = new();
//        public StubDispatcher Dispatcher { get; } = new();
//        public AgentRuntimeConfigurationPublisher Publisher { get; }

//        public Harness(string? encryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")
//        {
//            // The retry/versioning/manifest/ETag machinery these tests
//            // actually exercise now lives in the shared writer; the
//            // publisher above it only decides what goes into a version
//            // blob. Both are built here so the tests keep covering the
//            // whole path end to end, unchanged by the extraction.
//            var writer = new RuntimeConfigurationWriter<AgentConfigurationEntity>(
//                Blobs,
//                Configurations,
//                Dispatcher,
//                Options.Create(new CredentialEncryptionOptions { Key = encryptionKey ?? "" }),
//                NullLogger<RuntimeConfigurationWriter<AgentConfigurationEntity>>.Instance);

//            Publisher = new AgentRuntimeConfigurationPublisher(Projector, Blobs, Events, writer);
//        }
//    }

//    private static int VersionOf(FakeBlobStorageClient blobs, int version)
//    {
//        var bytes = blobs.Get(AgentConfigBlob.ContainerName,
//            AgentConfigBlob.VersionBlobName(Scoped, version));

//        Assert.NotNull(bytes);
//        return version;
//    }

//    [Fact]
//    public async Task FirstPublishWritesVersionOneAManifestAndTheFlatBlob()
//    {
//        var h = new Harness();

//        var result = await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        Assert.NotNull(result);
//        Assert.True(result!.Published);

//        VersionOf(h.Blobs, 1);
//        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName(Scoped)));
//        Assert.NotNull(h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.BlobName(Scoped)));
//    }

//    [Fact]
//    public async Task ManifestPointsAtTheVersionJustWritten()
//    {
//        var h = new Harness();

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        var manifest = JsonSerializer.Deserialize<ConfigurationManifest>(
//            h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName(Scoped))!)!;

//        Assert.Equal(1, manifest.ConfigurationVersion);
//        Assert.Equal(AgentConfigBlob.VersionBlobName(Scoped, 1), manifest.ConfigurationUri);
//    }

//    // The no-op guard: republishing identical content must not burn a
//    // version number. This is what stops a dashboard "Publish" click from
//    // inflating the history when nothing changed.
//    [Fact]
//    public async Task RepublishingIdenticalContentIsANoOp()
//    {
//        var h = new Harness();

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);
//        var second = await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        Assert.False(second!.Published);
//        Assert.Contains("unchanged", second.Reason, StringComparison.OrdinalIgnoreCase);
//        Assert.Null(h.Blobs.Get(AgentConfigBlob.ContainerName,
//            AgentConfigBlob.VersionBlobName(Scoped, 2)));
//    }

//    // The Agent half of the same regression the Device tests cover: the
//    // hash used to be computed over the ENCRYPTED classification settings,
//    // and CredentialCipher.Encrypt draws a fresh random AES-GCM nonce per
//    // call, so identical admin data hashed differently every time. The
//    // default fixture has no devices at all - and so no encryption - which
//    // is exactly why the guard appeared to work; this one carries an
//    // AccessToken so the encryption path actually runs.
//    [Fact]
//    public async Task TheNoOpGuardStillHoldsWhenACredentialFieldIsPresent()
//    {
//        var h = new Harness();
//        h.Projector.Document = h.Projector.Document with
//        {
//            Devices =
//            [
//                new AiDeviceClassificationEntryDto(
//                    "device-1",
//                    new Dictionary<string, string>
//                    {
//                        ["Endpoint"] = "https://detect/v1",
//                        ["AccessToken"] = "hunter2"
//                    },
//                    null)
//            ]
//        };

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);
//        var second = await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        Assert.False(second!.Published);
//        Assert.Contains("unchanged", second.Reason, StringComparison.OrdinalIgnoreCase);

//        // ...and the token itself is still ciphertext on the wire.
//        var json = System.Text.Encoding.UTF8.GetString(
//            h.Blobs.Get(AgentConfigBlob.ContainerName,
//                AgentConfigBlob.VersionBlobName(Scoped, 1))!);

//        Assert.DoesNotContain("hunter2", json, StringComparison.Ordinal);
//        Assert.Contains("enc:v1:", json, StringComparison.Ordinal);
//    }

//    // ADR-087: Name is hashed alongside AiClassification precisely so a
//    // Name-only change is not swallowed by the guard above.
//    [Fact]
//    public async Task ChangingOnlyTheNameStillPublishesANewVersion()
//    {
//        var h = new Harness();

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);
//        h.Projector.Document = h.Projector.Document with { Name = "Renamed Agent" };

//        var second = await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        Assert.True(second!.Published);
//        VersionOf(h.Blobs, 2);
//    }

//    [Fact]
//    public async Task VersionsAreMonotonicAndOlderVersionsAreNeverOverwritten()
//    {
//        var h = new Harness();

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);
//        var v1 = h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 1))!;

//        h.Projector.Document = h.Projector.Document with { Name = "Second" };
//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        var v1After = h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 1))!;

//        Assert.Equal(v1, v1After);
//        VersionOf(h.Blobs, 2);
//    }

//    // The retry loop exists for exactly this: another publisher updated the
//    // metadata row first, so our ETag is stale and Azure answers 412.
//    [Fact]
//    public async Task AStaleMetadataETagIsRetriedRatherThanSurfaced()
//    {
//        var h = new Harness();

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        h.Projector.Document = h.Projector.Document with { Name = "Second" };
//        h.Configurations.FailNextUpdateWithPreconditionFailed = true;

//        var result = await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        Assert.True(result!.Published);
//        Assert.True(h.Configurations.UpdateCalls >= 2);
//    }

//    [Fact]
//    public async Task WarningsBlockPublishingEntirely()
//    {
//        var h = new Harness();
//        h.Projector.Document = h.Projector.Document with { Warnings = ["RuntimeAgentId is not set"] };

//        var result = await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        Assert.False(result!.Published);
//        Assert.Empty(h.Blobs.Uploads);
//    }

//    // ADR-085: a missing encryption key blocks the publish outright rather
//    // than silently falling back to writing credentials in plaintext.
//    [Fact]
//    public async Task AMissingEncryptionKeyBlocksPublishRatherThanFallingBackToPlaintext()
//    {
//        var h = new Harness(encryptionKey: null);

//        var result = await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        Assert.False(result!.Published);
//        Assert.Contains("CredentialEncryption", result.Reason!, StringComparison.OrdinalIgnoreCase);
//        Assert.Empty(h.Blobs.Uploads);
//    }

//    [Fact]
//    public async Task PublishWritesAnAuditEventAndAsksForARestart()
//    {
//        var h = new Harness();

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        Assert.Single(h.Events.Written);
//        Assert.Equal(AgentEventTypes.ConfigPublished, h.Events.Written[0].EventType);
//        Assert.Equal(1, h.Dispatcher.Dispatches);
//    }

//    // ADR-070: rollback republishes an old version's content as a NEW
//    // version and never mutates the old blob, so history stays append-only.
//    [Fact]
//    public async Task RollbackCreatesANewVersionRatherThanMutatingTheOldOne()
//    {
//        var h = new Harness();

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);
//        var v1 = h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 1))!;

//        h.Projector.Document = h.Projector.Document with { Name = "Second" };
//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        var result = await h.Publisher.RollbackAsync(Tenant, AdminAgentId, 1);

//        Assert.True(result!.Published);
//        VersionOf(h.Blobs, 3);
//        Assert.Equal(v1, h.Blobs.Get(AgentConfigBlob.ContainerName, AgentConfigBlob.VersionBlobName(Scoped, 1))!);
//        Assert.Equal(AgentEventTypes.ConfigRolledBack, h.Events.Written[^1].EventType);
//    }

//    [Fact]
//    public async Task RollingBackToAVersionThatDoesNotExistIsRejected()
//    {
//        var h = new Harness();

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        var result = await h.Publisher.RollbackAsync(Tenant, AdminAgentId, 99);

//        Assert.False(result!.Published);
//        Assert.Contains("does not exist", result.Reason!, StringComparison.OrdinalIgnoreCase);
//    }

//    // Rollback deliberately bypasses the no-op guard: rolling back to the
//    // content you are already running is still a real, recorded action.
//    [Fact]
//    public async Task RollbackToCurrentContentStillCreatesAVersion()
//    {
//        var h = new Harness();

//        await h.Publisher.PublishAsync(Tenant, AdminAgentId);

//        var result = await h.Publisher.RollbackAsync(Tenant, AdminAgentId, 1);

//        Assert.True(result!.Published);
//        VersionOf(h.Blobs, 2);
//    }
//}
