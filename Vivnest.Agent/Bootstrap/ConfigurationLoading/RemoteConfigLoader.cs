using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Vivnest.Core.Configuration;
using Vivnest.Core.Constants;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Agent.Bootstrap.ConfigurationLoading;

// Fetches the shared and per-agent configuration documents from blob
// storage. Every failure is non-fatal: a missing blob is informational
// (a fresh agent legitimately has none), anything else is reported and
// the load continues on whatever local configuration exists.
internal sealed class RemoteConfigLoader
{
    private readonly ConfigurationManager _configuration;
    private readonly ConfigDecryptor _decryptor;
    private readonly StartupErrorSink _errors;

    public RemoteConfigLoader(
        ConfigurationManager configuration,
        ConfigDecryptor decryptor,
        StartupErrorSink errors)
    {
        _configuration = configuration;
        _decryptor = decryptor;
        _errors = errors;
    }

    public async Task TryLoadSharedConfigAsync()
    {
        var storageConnectionString =
            _configuration["Storage:ConnectionString"];

        if (string.IsNullOrWhiteSpace(storageConnectionString))
        {
            Console.WriteLine(
                "[Startup] Storage:ConnectionString not set; " +
                "skipping shared config fetch.");

            return;
        }

        try
        {
            var blobClient =
                new AzureBlobStorageClient(
                    new BlobServiceClient(storageConnectionString));

            var configBytes =
                await blobClient.DownloadAsync(
                    SharedConfigBlob.ContainerName,
                    SharedConfigBlob.BlobName);

            configBytes =
                _decryptor.Decrypt(configBytes);

            ConfigSourceInsertion.InsertJsonBeforeEnvVars(
                _configuration,
                configBytes);

            Console.WriteLine(
                "[Startup] Loaded remote shared config.");
        }
        catch (RequestFailedException ex)
            when (ex.Status == 404)
        {
            Console.WriteLine(
                "[Startup] No remote shared config blob found; " +
                "using local/per-agent config only.");
        }
        catch (Exception ex)
        {
            _errors.Report(
                "[Startup] Failed to load remote shared config, " +
                $"continuing without it: {ex.Message}");
        }
    }

    public async Task TryLoadAgentConfigAsync()
    {
        var agentId = _configuration["Agent:AgentId"];
        var storageConnectionString =
            _configuration["Storage:ConnectionString"];

        if (string.IsNullOrWhiteSpace(agentId) ||
            string.IsNullOrWhiteSpace(storageConnectionString))
        {
            Console.WriteLine(
                "[Startup] Agent:AgentId or Storage:ConnectionString " +
                "not set; skipping remote config fetch.");

            return;
        }

        try
        {
            var blobClient =
                new AzureBlobStorageClient(
                    new BlobServiceClient(storageConnectionString));

            var key = new ConfigBlobKey(
                _configuration["Agent:TenantId"] ?? "",
                _configuration["Agent:SiteId"] ?? "",
                agentId);

            var configBytes =
                await TryLoadViaManifestAsync(
                    blobClient,
                    AgentConfigBlob.ContainerName,
                    AgentConfigBlob.ManifestBlobName(key));

            var source = "the versioned manifest";

            if (configBytes == null)
            {
                configBytes =
                    await blobClient.DownloadAsync(
                        AgentConfigBlob.ContainerName,
                        AgentConfigBlob.BlobName(key));

                source = "the flat blob";
            }

            configBytes =
                _decryptor.Decrypt(configBytes);

            ConfigSourceInsertion.InsertJsonBeforeEnvVars(
                _configuration,
                configBytes);

            Console.WriteLine(
                $"[Startup] Loaded remote config for agent {agentId} " +
                $"(via {source}).");
        }
        catch (RequestFailedException ex)
            when (ex.Status == 404)
        {
            Console.WriteLine(
                $"[Startup] No remote config blob found for agent {agentId}; " +
                "using local config only.");
        }
        catch (Exception ex)
        {
            _errors.Report(
                $"[Startup] Failed to load remote config for agent {agentId}, " +
                $"continuing with local config only: {ex.Message}");
        }
    }

    private static async Task<byte[]?> TryLoadViaManifestAsync(
        AzureBlobStorageClient blobClient,
        string containerName,
        string manifestBlobName)
    {
        byte[] manifestBytes;

        try
        {
            manifestBytes =
                await blobClient.DownloadAsync(
                    containerName,
                    manifestBlobName);
        }
        catch (RequestFailedException ex)
            when (ex.Status == 404)
        {
            return null;
        }

        var manifest =
            JsonSerializer.Deserialize<ConfigurationManifest>(
                manifestBytes);

        if (manifest == null)
        {
            return null;
        }

        return await blobClient.DownloadAsync(
            containerName,
            manifest.ConfigurationUri);
    }
}
