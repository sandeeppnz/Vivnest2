using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Seeding;
using Vivnest.Cloud.Options;
using Vivnest.Core.Options;
using Vivnest.Core.Security;
using Vivnest.Core.Storage;

namespace Vivnest.Tests;

// Shared-config self-publishing (ADR-120). The load-bearing test is the
// round trip: the published document must bind back through the real
// configuration binder into the same option values the code defaults
// declare - section names, property names, TimeSpan formatting and the
// enc:v1 connection string all included. That is the exact contract the
// hand-assembled blob silently broke three times during the 2026-08-29
// rebuild.
public class SharedConfigPublisherTests
{
    private const string ConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=fake;AccountKey=abc123==;EndpointSuffix=core.windows.net";

    private static readonly string EncryptionKey =
        Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

    [Fact]
    public async Task PublishedDocumentBindsBackToTheCodeDefaults()
    {
        var blobs = new CapturingBlobClient();
        var publisher = new SharedConfigPublisher(
            Options.Create(new StorageOptions { ConnectionString = ConnectionString }),
            Options.Create(new CredentialEncryptionOptions { Key = EncryptionKey }),
            blobs);

        var result = await publisher.PublishAsync();

        Assert.True(result.Success, result.Error);
        Assert.Equal("shared-config", result.Container);
        Assert.Equal("common-config.json", result.BlobName);
        Assert.NotNull(blobs.Uploaded);

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(blobs.Uploaded!))
            .Build();

        // Tables and Messaging bind back to exactly the code defaults.
        var tables = configuration.GetSection("Tables").Get<TablesOptions>()!;
        Assert.Equal(new TablesOptions().AgentHeartbeat, tables.AgentHeartbeat);
        Assert.Equal(new TablesOptions().AgentAlertState, tables.AgentAlertState);

        var messaging = configuration.GetSection("Messaging").Get<MessagingOptions>()!;
        Assert.Equal("camera-captured", messaging.CameraCapturedQueue);
        Assert.Equal("agent-commands", messaging.AgentCommandQueue);

        // The connection string travels encrypted and decrypts to the
        // original - the ADR-085 contract the Agent's ConfigDecryptor
        // relies on.
        Assert.StartsWith("enc:v1:", messaging.ConnectionString);
        Assert.Equal(
            ConnectionString,
            Decrypt(messaging.ConnectionString));

        Assert.Equal("photos", configuration["Storage:BlobContainer"]);

        // The Enabled-defaults-false trap: every telemetry section must
        // publish as explicitly on, at a sane interval, because deployed
        // agents predating the in-code defaults bind false for a missing
        // section.
        var agentHeartbeat = configuration.GetSection("AgentHeartbeat").Get<AgentHeartbeatOptions>()!;
        Assert.True(agentHeartbeat.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(1), agentHeartbeat.HeartbeatInterval);

        var deviceHeartbeat = configuration.GetSection("DeviceHeartbeat").Get<DeviceHeartbeatOptions>()!;
        Assert.True(deviceHeartbeat.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(1), deviceHeartbeat.HeartbeatInterval);

        var metrics = configuration.GetSection("AgentMetrics").Get<AgentMetricsOptions>()!;
        Assert.True(metrics.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(1), metrics.Interval);

        Assert.True(configuration.GetSection("AgentEvents").Get<AgentEventOptions>()!.Enabled);
        Assert.True(configuration.GetSection("DeviceEvents").Get<DeviceEventOptions>()!.Enabled);
    }

    [Fact]
    public async Task RefusesToPublishWithoutAConnectionString()
    {
        var blobs = new CapturingBlobClient();
        var publisher = new SharedConfigPublisher(
            Options.Create(new StorageOptions()),
            Options.Create(new CredentialEncryptionOptions { Key = EncryptionKey }),
            blobs);

        var result = await publisher.PublishAsync();

        Assert.False(result.Success);
        Assert.Contains("Storage:ConnectionString", result.Error);
        Assert.Null(blobs.Uploaded);
    }

    [Fact]
    public async Task RefusesToPublishWithoutAnEncryptionKey()
    {
        var blobs = new CapturingBlobClient();
        var publisher = new SharedConfigPublisher(
            Options.Create(new StorageOptions { ConnectionString = ConnectionString }),
            Options.Create(new CredentialEncryptionOptions()),
            blobs);

        var result = await publisher.PublishAsync();

        Assert.False(result.Success);
        Assert.Contains("CredentialEncryption:Key", result.Error);
        Assert.Null(blobs.Uploaded);
    }

    [Fact]
    public void CodeDefaultTableNamesAreUniqueAndCanonicallyPrefixed()
    {
        var names = typeof(TablesOptions).GetProperties()
            .Select(p => (string)p.GetValue(new TablesOptions())!)
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n => Assert.StartsWith("tbl", n));
    }

    // Several queue names are ALSO compile-time literals in [QueueTrigger]
    // attributes and publisher constants (see MessagingOptions' own
    // comments) - pin the defaults to those literals so neither side can
    // drift alone.
    [Fact]
    public void CodeDefaultQueueNamesMatchTheHardcodedTriggerLiterals()
    {
        var defaults = new MessagingOptions();

        Assert.Equal("agent-restart-commands", defaults.RestartCommandQueue);
        Assert.Equal("agent-deploy-commands", defaults.DeployCommandQueue);
        Assert.Equal("classify-requests", defaults.ClassifyRequestQueue);
        Assert.Equal("agent-classify-commands", defaults.ClassifyCommandQueue);
        Assert.Equal("agent-commands", defaults.AgentCommandQueue);
    }

    private static string Decrypt(string encrypted)
    {
        // Round-trip through the same blind JSON-walk the Agent uses.
        var node = System.Text.Json.Nodes.JsonNode.Parse(
            $"{{\"v\": \"{encrypted}\"}}")!;

        CredentialCipher.DecryptInPlace(node, CredentialCipher.ParseKey(EncryptionKey));

        return node["v"]!.GetValue<string>();
    }

    private sealed class CapturingBlobClient : IBlobStorageClient
    {
        public byte[]? Uploaded { get; private set; }

        public async Task UploadAsync(
            string containerName, string blobName, Stream content,
            Azure.Storage.Blobs.Models.BlobHttpHeaders? httpHeaders = null,
            bool failIfExists = false, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            Uploaded = memory.ToArray();
        }

        public Task<byte[]> DownloadAsync(
            string containerName, string blobName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListBlobNamesAsync(
            string containerName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(
            string containerName, string blobName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Uri GenerateReadSasUri(
            string containerName, string blobName, TimeSpan validFor, string? cacheControl = null) =>
            throw new NotSupportedException();
    }
}
