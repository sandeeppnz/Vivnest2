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

// The Device half of the same pipeline the Agent tests cover. Worth having
// separately rather than trusting "it's the same code now": the two sides
// differ in exactly the place a de-duplication is most likely to break
// something, namely what gets written to the legacy flat blob (the Device
// side reuses the versioned bytes verbatim; the Agent side merge-patches
// its own keys onto whatever was already there).
public class DeviceConfigurationPublisherTests
{
    private const string RuntimeDeviceId = "device-runtime-1";
    private const string AdminDeviceId = "device-admin-1";
    private const string OwningAgentId = "agent-runtime-1";
    private static readonly TenantContext Tenant = new("tenant-1", "site-1", DevicesOnly: false);

    // Publishes are scoped by tenant/site (ADR-091). Since the legacy
    // mirror was removed on 2026-08-21, these assertions have to name the
    // scoped blobs - previously they passed against the unscoped names
    // only because every publish wrote both.
    private static ConfigBlobKey Scoped =>
        new(Tenant.TenantId, Tenant.SiteId, RuntimeDeviceId);

    private sealed class StubProjector : IDeviceRuntimeConfigurationProjector
    {
        public DeviceRuntimeConfigurationDocumentDto Document { get; set; } =
            new(RuntimeDeviceId, "Kitchen Camera", "Camera", true, "Kitchen", "Hikvision", "DS-2CD", "1.0",
                OwningAgentId,
                new Dictionary<string, string> { ["RtspUrl"] = "rtsp://cam/1", ["RtspPassword"] = "hunter2" },
                [], []);

        public Task<DeviceRuntimeConfigurationDocumentDto?> ProjectAsync(
            TenantContext tenant, string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceRuntimeConfigurationDocumentDto?>(Document);
    }

    private sealed class StubDispatcher : ICommandDispatcher
    {
        public List<string> RestartedAgents { get; } = [];

        public Task<AgentCommandDto?> DispatchAsync(
            TenantContext tenant, string commandType, string targetAgentId, string requestedBy,
            string? targetDeviceId = null, string? capabilityId = null, string? payload = null,
            CancellationToken cancellationToken = default)
        {
            RestartedAgents.Add(targetAgentId);
            return Task.FromResult<AgentCommandDto?>(null);
        }
    }

    private sealed class Harness
    {
        public StubProjector Projector { get; } = new();
        public FakeBlobStorageClient Blobs { get; } = new();
        public FakeDeviceConfigurationStore Configurations { get; } = new();
        public FakeDeviceEventStore Events { get; } = new();
        public StubDispatcher Dispatcher { get; } = new();
        public DeviceRuntimeConfigurationPublisher Publisher { get; }

        public Harness(string? encryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")
        {
            var writer = new RuntimeConfigurationWriter<DeviceConfigurationEntity>(
                Blobs,
                Configurations,
                Dispatcher,
                Options.Create(new CredentialEncryptionOptions { Key = encryptionKey ?? "" }),
                NullLogger<RuntimeConfigurationWriter<DeviceConfigurationEntity>>.Instance);

            Publisher = new DeviceRuntimeConfigurationPublisher(Projector, Blobs, Events, writer);
        }
    }

    private static byte[] Version(FakeBlobStorageClient blobs, int version)
    {
        var bytes = blobs.Get(DeviceConfigBlob.ContainerName,
            DeviceConfigBlob.VersionBlobName(Scoped, version));

        Assert.NotNull(bytes);
        return bytes!;
    }

    [Fact]
    public async Task FirstPublishWritesVersionOneAManifestAndTheFlatBlob()
    {
        var h = new Harness();

        var result = await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.NotNull(result);
        Assert.True(result!.Published);

        Version(h.Blobs, 1);
        Assert.NotNull(h.Blobs.Get(DeviceConfigBlob.ContainerName, DeviceConfigBlob.ManifestBlobName(Scoped)));
        Assert.NotNull(h.Blobs.Get(DeviceConfigBlob.ContainerName, DeviceConfigBlob.BlobName(Scoped)));
    }

    // The one behaviour that genuinely differs from the Agent side: the
    // legacy flat blob is the versioned document byte-for-byte, not a
    // merge-patch.
    [Fact]
    public async Task TheFlatBlobIsTheVersionedDocumentVerbatim()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.Equal(
            Version(h.Blobs, 1),
            h.Blobs.Get(DeviceConfigBlob.ContainerName, DeviceConfigBlob.BlobName(Scoped)));
    }

    [Fact]
    public async Task ManifestPointsAtTheVersionJustWritten()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        var manifest = JsonSerializer.Deserialize<ConfigurationManifest>(
            h.Blobs.Get(DeviceConfigBlob.ContainerName, DeviceConfigBlob.ManifestBlobName(Scoped))!)!;

        Assert.Equal(1, manifest.ConfigurationVersion);
        Assert.Equal(DeviceConfigBlob.VersionBlobName(Scoped, 1), manifest.ConfigurationUri);
    }

    // ADR-085: credential-shaped Settings keys go out as enc:v1: ciphertext,
    // never plaintext - the published blob must not contain the password.
    [Fact]
    public async Task CredentialShapedSettingsAreEncryptedInThePublishedBlob()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        var json = System.Text.Encoding.UTF8.GetString(Version(h.Blobs, 1));

        Assert.DoesNotContain("hunter2", json, StringComparison.Ordinal);
        Assert.Contains("enc:v1:", json, StringComparison.Ordinal);
        Assert.Contains("rtsp://cam/1", json, StringComparison.Ordinal);
    }

    // The no-op guard, and the regression test for the defect these tests originally caught:
    // the hash used to be computed over the ENCRYPTED settings, and
    // CredentialCipher.Encrypt draws a fresh random AES-GCM nonce per call,
    // so identical admin data hashed differently every time. Any device
    // with a credential-shaped key - RtspPassword, i.e. every real camera -
    // therefore never hit the no-op guard, and every dashboard Publish
    // click burned a version and restarted the owning agent. The default
    // fixture above carries an RtspPassword precisely so this is the case
    // being tested.
    [Fact]
    public async Task TheNoOpGuardStillHoldsWhenACredentialFieldIsPresent()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);
        var second = await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.False(second!.Published);
        Assert.Contains("unchanged", second.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(h.Blobs.Get(DeviceConfigBlob.ContainerName,
            DeviceConfigBlob.VersionBlobName(Scoped, 2)));
    }

    // ... and the ciphertext itself must still differ between two
    // publishes of the same data, or the nonce would not be random.
    [Fact]
    public async Task ChangingACredentialValueStillPublishesANewVersion()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        h.Projector.Document = h.Projector.Document with
        {
            Settings = new Dictionary<string, string>
            {
                ["RtspUrl"] = "rtsp://cam/1",
                ["RtspPassword"] = "hunter3"
            }
        };

        var second = await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.True(second!.Published);
        Version(h.Blobs, 2);
    }

    [Fact]
    public async Task VersionsAreMonotonicAndOlderVersionsAreNeverOverwritten()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);
        var v1 = Version(h.Blobs, 1);

        h.Projector.Document = h.Projector.Document with { Location = "Pantry" };
        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.Equal(v1, Version(h.Blobs, 1));
        Version(h.Blobs, 2);
    }

    [Fact]
    public async Task AStaleMetadataETagIsRetriedRatherThanSurfaced()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        h.Projector.Document = h.Projector.Document with { Location = "Pantry" };
        h.Configurations.FailNextUpdateWithPreconditionFailed = true;

        var result = await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.True(result!.Published);
        Assert.True(h.Configurations.UpdateCalls >= 2);
    }

    [Fact]
    public async Task WarningsBlockPublishingEntirely()
    {
        var h = new Harness();
        h.Projector.Document = h.Projector.Document with { Warnings = ["RuntimeDeviceId is not set"] };

        var result = await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.False(result!.Published);
        Assert.Empty(h.Blobs.Uploads);
    }

    [Fact]
    public async Task AMissingEncryptionKeyBlocksPublishRatherThanFallingBackToPlaintext()
    {
        var h = new Harness(encryptionKey: null);

        var result = await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.False(result!.Published);
        Assert.Contains("CredentialEncryption", result.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Blobs.Uploads);
    }

    // ADR-068: the restart goes to the device's OWNING agent, not the
    // device - the one place the Device side passes a different id into the
    // shared restart dispatch than the Agent side does.
    [Fact]
    public async Task PublishWritesAnAuditEventAndRestartsTheOwningAgent()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.Single(h.Events.Written);
        Assert.Equal(DeviceEventTypes.ConfigPublished, h.Events.Written[0].EventType);
        Assert.Equal([OwningAgentId], h.Dispatcher.RestartedAgents);
    }

    // The null-OwningAgentId case the Agent side cannot produce: a device
    // with no owning agent publishes fine, it just has nothing to restart.
    [Fact]
    public async Task ADeviceWithNoOwningAgentStillPublishesAndRestartsNothing()
    {
        var h = new Harness();
        h.Projector.Document = h.Projector.Document with { OwningAgentId = null };

        var result = await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        Assert.True(result!.Published);
        Assert.Empty(h.Dispatcher.RestartedAgents);
    }

    // ADR-070: rollback republishes an old version's content as a NEW
    // version and never mutates the old blob.
    [Fact]
    public async Task RollbackCreatesANewVersionRatherThanMutatingTheOldOne()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);
        var v1 = Version(h.Blobs, 1);

        h.Projector.Document = h.Projector.Document with { Location = "Pantry" };
        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        var result = await h.Publisher.RollbackAsync(Tenant, AdminDeviceId, 1);

        Assert.True(result!.Published);
        Version(h.Blobs, 3);
        Assert.Equal(v1, Version(h.Blobs, 1));
        Assert.Equal(DeviceEventTypes.ConfigRolledBack, h.Events.Written[^1].EventType);

        // The returned document reflects what was rolled back to, not
        // today's live projection.
        Assert.Equal("Kitchen", result.Document.Location);
    }

    [Fact]
    public async Task RollingBackToAVersionThatDoesNotExistIsRejected()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        var result = await h.Publisher.RollbackAsync(Tenant, AdminDeviceId, 99);

        Assert.False(result!.Published);
        Assert.Contains("does not exist", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RollbackToCurrentContentStillCreatesAVersion()
    {
        var h = new Harness();

        await h.Publisher.PublishAsync(Tenant, AdminDeviceId);

        var result = await h.Publisher.RollbackAsync(Tenant, AdminDeviceId, 1);

        Assert.True(result!.Published);
        Version(h.Blobs, 2);
    }
}
