using System.Text.Json;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Options;
using Vivnest.Core.Constants;
using Vivnest.Core.Options;
using Vivnest.Core.Security;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Admin.Seeding;

// Generates shared-config/common-config.json (ADR-120), replacing the
// hand-assembled blob that took three iterations to get right during the
// 2026-08-29 factory-reset rebuild (missing Tables crashed the Agent on
// CreateIfNotExists(""); missing Enabled flags silently disabled every
// heartbeat). The document is produced by serializing freshly-constructed
// option instances - the very classes the Agent binds the blob back into -
// so the published values and the code defaults are one and the same by
// construction. The full document is still published (rather than just the
// connection string) because agents older than the in-code-defaults change
// need every section spelled out.
public sealed class SharedConfigPublisher : ISharedConfigPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IOptions<StorageOptions> _storage;
    private readonly IOptions<CredentialEncryptionOptions> _credentialEncryption;
    private readonly IBlobStorageClient _blobClient;

    public SharedConfigPublisher(
        IOptions<StorageOptions> storage,
        IOptions<CredentialEncryptionOptions> credentialEncryption,
        IBlobStorageClient blobClient)
    {
        _storage = storage;
        _credentialEncryption = credentialEncryption;
        _blobClient = blobClient;
    }

    public async Task<SharedConfigPublishResult> PublishAsync(
        CancellationToken cancellationToken = default)
    {
        // Cloud's own storage connection string is the same account the
        // Agent's Messaging section points at - queues, tables and blobs
        // all live on one account.
        var connectionString = _storage.Value.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Failure(
                "Cannot publish: Storage:ConnectionString is not configured on the Cloud service.");
        }

        var configuredKey = _credentialEncryption.Value.Key;

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return Failure(
                "Cannot publish: CredentialEncryption:Key is not configured on the Cloud service - " +
                "the connection string cannot be encrypted (decision-log.md ADR-085).");
        }

        byte[] encryptionKey;

        try
        {
            encryptionKey = CredentialCipher.ParseKey(configuredKey);
        }
        catch (Exception ex)
        {
            return Failure($"Cannot publish: CredentialEncryption:Key is misconfigured ({ex.Message}).");
        }

        var document = BuildDocument(
            CredentialCipher.Encrypt(connectionString, encryptionKey));

        var json = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);

        using var stream = new MemoryStream(json);

        await _blobClient.UploadAsync(
            SharedConfigBlob.ContainerName,
            SharedConfigBlob.BlobName,
            stream,
            httpHeaders: null,
            failIfExists: false,
            cancellationToken);

        return new SharedConfigPublishResult(
            true, null,
            SharedConfigBlob.ContainerName,
            SharedConfigBlob.BlobName,
            [.. document.Keys],
            json.Length);
    }

    // The section names are the exact GetSection(...) keys both hosts
    // bind (AgentOptionsRegistration / Cloud.Functions Program.cs);
    // SharedConfigPublisherTests round-trips this document back through
    // the configuration binder to pin that contract.
    private static Dictionary<string, object> BuildDocument(string encryptedConnectionString) =>
        new()
        {
            ["Storage"] = new { new StorageOptions().BlobContainer },
            ["Tables"] = new TablesOptions(),
            ["AgentHeartbeat"] = new AgentHeartbeatOptions(),
            ["DeviceHeartbeat"] = new DeviceHeartbeatOptions(),
            ["AgentMetrics"] = new AgentMetricsOptions(),
            ["AgentEvents"] = new AgentEventOptions(),
            ["DeviceEvents"] = new DeviceEventOptions(),
            ["Messaging"] = new MessagingOptions { ConnectionString = encryptedConnectionString },
        };

    private static SharedConfigPublishResult Failure(string error) =>
        new(false, error, null, null, null, 0);
}
