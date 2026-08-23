using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vivnest.Core.Configuration;
using Vivnest.Core.Constants;
using Vivnest.Core.Security;
using Vivnest.Core.Storage;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Devices;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Agent.Bootstrap;

public static class AgentConfigurationLoader
{
    // Everything here runs BEFORE the host is built, so there is no
    // ILogger to write to - which is why this file talks to Console. The
    // cost of that was that the Agent's most failure-prone phase (fetch
    // remote config over the network, decrypt it, parse it) was visible
    // only to whoever could run `docker logs` on the box.
    //
    // Failures are now also accumulated here and written into
    // configuration as ConfigurationLoadErrors, which
    // AgentConfigMetadataOptions binds and AgentHeartbeatWorker sends to
    // Cloud on every heartbeat. Same mechanism the per-device errors
    // already used; it just covers the whole load now instead of one
    // method.
    //
    // Static because this class is static and LoadAsync runs exactly once,
    // single-threaded, before anything else exists. Cleared on entry so a
    // second call cannot inherit the first one's errors.
    private static readonly List<string> StartupErrors = [];

    // Reports a failure to both places: the console, for someone watching
    // the container, and the accumulator, for Cloud. Informational lines
    // ("... not set; skipping") deliberately stay plain Console.WriteLine -
    // a setting that was never configured is not a fault to report.
    private static void ReportError(string message)
    {
        Console.WriteLine(message);

        StartupErrors.Add(message.Replace("[Startup] ", ""));
    }

    public static async Task LoadAsync(
        ConfigurationManager configuration)
    {
        StartupErrors.Clear();

        // Credential encryption key must come from the bootstrap
        // configuration that already exists before remote configuration
        // is loaded.
        var credentialEncryptionKey =
            TryParseCredentialEncryptionKey(configuration);

        // Load either local or remote public configuration.
        if (configuration.GetValue<bool>("LoadLocalSettings"))
        {
            TryLoadLocalSharedConfig(
                configuration,
                credentialEncryptionKey);

            TryLoadLocalConfig(
                configuration,
                credentialEncryptionKey);
        }
        else
        {
            await TryLoadRemoteSharedConfigAsync(
                configuration,
                credentialEncryptionKey);

            await TryLoadRemoteConfigAsync(
                configuration,
                credentialEncryptionKey);
        }

        // Secrets are always local-only.
        TryLoadLocalSharedSecrets(configuration);

        TryLoadLocalAgentSecrets(
            configuration,
            configuration["Agent:AgentId"] ?? "");

        // Determine agent type.
        //
        // Defaults to Low to preserve the existing behaviour.
        var agentType = GetAgentType(configuration);

        // Low agents load their device configuration.
        if (agentType == AgentType.Low)
        {
            await TryLoadRemoteDeviceConfigsAsync(
                configuration,
                configuration["Agent:AgentId"] ?? "",
                credentialEncryptionKey);
        }

        PublishStartupErrors(configuration);
    }

    // Last, so it sees every phase's failures. AgentConfigMetadataOptions
    // binds ConfigurationLoadErrors and AgentHeartbeatWorker sends it to
    // Cloud as ConfigurationLoadError on every heartbeat - so a startup
    // problem that used to exist only in the container's stdout now
    // reaches the dashboard.
    private static void PublishStartupErrors(
        ConfigurationManager configuration)
    {
        if (StartupErrors.Count == 0)
            return;

        var root =
            new JsonObject
            {
                ["ConfigurationLoadErrors"] =
                    new JsonArray(
                        StartupErrors
                            .Select(e => (JsonNode?)JsonValue.Create(e))
                            .ToArray())
            };

        InsertConfigSourceBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource
            {
                Stream =
                    new ReusableMemoryStream(
                        JsonSerializer.SerializeToUtf8Bytes(root))
            });

        Console.WriteLine(
            $"[Startup] {StartupErrors.Count} configuration load error(s) " +
            "will be reported to Cloud on the next heartbeat.");
    }

    private static AgentType GetAgentType(
        IConfiguration configuration)
    {
        return Enum.TryParse<AgentType>(
            configuration["Agent:Type"],
            out var parsedType)
                ? parsedType
                : AgentType.Low;
    }

    // ---------------------------------------------------------------------
    // Remote shared configuration
    // ---------------------------------------------------------------------

    private static async Task TryLoadRemoteSharedConfigAsync(
        ConfigurationManager configuration,
        byte[]? credentialEncryptionKey)
    {
        var storageConnectionString =
            configuration["Storage:ConnectionString"];

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
                DecryptConfigBytes(
                    configBytes,
                    credentialEncryptionKey);

            InsertConfigSourceBeforeEnvVars(
                configuration,
                new JsonStreamConfigurationSource
                {
                    Stream = new ReusableMemoryStream(configBytes)
                });

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
            ReportError(
                "[Startup] Failed to load remote shared config, " +
                $"continuing without it: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // Remote agent configuration
    // ---------------------------------------------------------------------

    private static async Task TryLoadRemoteConfigAsync(
        ConfigurationManager configuration,
        byte[]? credentialEncryptionKey)
    {
        var agentId = configuration["Agent:AgentId"];
        var storageConnectionString =
            configuration["Storage:ConnectionString"];

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
                configuration["Agent:TenantId"] ?? "",
                configuration["Agent:SiteId"] ?? "",
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
                DecryptConfigBytes(
                    configBytes,
                    credentialEncryptionKey);

            InsertConfigSourceBeforeEnvVars(
                configuration,
                new JsonStreamConfigurationSource
                {
                    Stream = new ReusableMemoryStream(configBytes)
                });

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
            ReportError(
                $"[Startup] Failed to load remote config for agent {agentId}, " +
                $"continuing with local config only: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // Manifest loading
    // ---------------------------------------------------------------------

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

    // ---------------------------------------------------------------------
    // Remote device configuration
    // ---------------------------------------------------------------------

    private static async Task TryLoadRemoteDeviceConfigsAsync(
        ConfigurationManager configuration,
        string agentId,
        byte[]? credentialEncryptionKey)
    {
        var storageConnectionString =
            configuration["Storage:ConnectionString"];

        if (string.IsNullOrWhiteSpace(agentId) ||
            string.IsNullOrWhiteSpace(storageConnectionString))
        {
            Console.WriteLine(
                "[Startup] Agent:AgentId or Storage:ConnectionString " +
                "not set; skipping device config fetch.");

            return;
        }

        try
        {
            var blobClient =
                new AzureBlobStorageClient(
                    new BlobServiceClient(storageConnectionString));

            var allBlobNames =
                await blobClient.ListBlobNamesAsync(
                    DeviceConfigBlob.ContainerName);

            var devices = new JsonArray();

            var prefix =
                DeviceConfigBlob.Prefix(
                    configuration["Agent:TenantId"] ?? "",
                    configuration["Agent:SiteId"] ?? "");

            var scopedNames =
                prefix.Length == 0
                    ? []
                    : allBlobNames
                        .Where(n =>
                            n.StartsWith(
                                prefix,
                                StringComparison.Ordinal))
                        .Select(n => n[prefix.Length..])
                        .ToList();

            var usingScopedLayout =
                scopedNames.Count > 0;

            var blobNames =
                usingScopedLayout
                    ? scopedNames
                    : allBlobNames.ToList();

            var blobPrefix =
                usingScopedLayout
                    ? prefix
                    : "";

            Console.WriteLine(
                usingScopedLayout
                    ? $"[Startup] Reading device configs from " +
                      $"the scoped layout ({prefix}), " +
                      $"{blobNames.Count} blob(s)."
                    : $"[Startup] No device configs under the scoped " +
                      $"prefix; falling back to the flat container " +
                      $"listing ({blobNames.Count} blob(s)).");

            // The shared accumulator, not a local list - these
            // per-device failures and the whole-load failures below are
            // reported through one channel now.
            var loadErrors = StartupErrors;

            const string ManifestSuffix = "/current.json";

            var manifestBlobNames =
                blobNames
                    .Where(n =>
                        n.EndsWith(
                            ManifestSuffix,
                            StringComparison.Ordinal)
                        && n.Count(c => c == '/') == 1)
                    .ToList();

            var legacyBlobNames =
                blobNames
                    .Where(n => !n.Contains('/'))
                    .ToList();

            var processedDeviceIds =
                new HashSet<string>(
                    StringComparer.Ordinal);

            // -------------------------------------------------------------
            // Versioned / manifest-driven devices
            // -------------------------------------------------------------

            foreach (var manifestBlobName in manifestBlobNames)
            {
                var deviceId =
                    manifestBlobName[
                        ..^ManifestSuffix.Length];

                byte[] manifestBytes;

                try
                {
                    manifestBytes =
                        await blobClient.DownloadAsync(
                            DeviceConfigBlob.ContainerName,
                            blobPrefix + manifestBlobName);
                }
                catch (Exception ex)
                {
                    ReportError(
                        $"[Startup] Failed to download device manifest " +
                        $"{manifestBlobName}, skipping: {ex.Message}");

                    continue;
                }

                ConfigurationManifest? manifest;

                try
                {
                    manifest =
                        JsonSerializer.Deserialize<ConfigurationManifest>(
                            manifestBytes);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine(
                        $"[Startup] Device manifest " +
                        $"{manifestBlobName} is not valid JSON, " +
                        $"skipping: {ex.Message}");

                    continue;
                }

                if (manifest == null)
                {
                    Console.WriteLine(
                        $"[Startup] Device manifest " +
                        $"{manifestBlobName} deserialized to null, " +
                        "skipping.");

                    continue;
                }

                byte[] versionBytes;

                try
                {
                    versionBytes =
                        await blobClient.DownloadAsync(
                            DeviceConfigBlob.ContainerName,
                            manifest.ConfigurationUri);
                }
                catch (Exception ex)
                {
                    ReportError(
                        $"[Startup] Failed to download device version blob " +
                        $"{manifest.ConfigurationUri} " +
                        $"(from manifest {manifestBlobName}), " +
                        $"skipping: {ex.Message}");

                    continue;
                }

                if (TryProcessDeviceBlob(
                    versionBytes,
                    manifestBlobName,
                    agentId,
                    deviceId,
                    loadErrors,
                    credentialEncryptionKey,
                    out var deviceObject))
                {
                    devices.Add(deviceObject);
                    processedDeviceIds.Add(deviceId);
                }
            }

            // -------------------------------------------------------------
            // Legacy flat device blobs
            // -------------------------------------------------------------

            foreach (var blobName in legacyBlobNames)
            {
                var deviceId =
                    blobName[..^".json".Length];

                if (processedDeviceIds.Contains(deviceId))
                {
                    continue;
                }

                byte[] deviceBytes;

                try
                {
                    deviceBytes =
                        await blobClient.DownloadAsync(
                            DeviceConfigBlob.ContainerName,
                            blobPrefix + blobName);
                }
                catch (Exception ex)
                {
                    ReportError(
                        $"[Startup] Failed to download device config " +
                        $"blob {blobName}, skipping: {ex.Message}");

                    continue;
                }

                if (TryProcessDeviceBlob(
                    deviceBytes,
                    blobName,
                    agentId,
                    deviceId,
                    loadErrors,
                    credentialEncryptionKey,
                    out var deviceObject))
                {
                    devices.Add(deviceObject);
                }
            }

            var root =
                new JsonObject
                {
                    ["Devices"] = devices
                };

            var jsonBytes =
                JsonSerializer.SerializeToUtf8Bytes(root);

            InsertConfigSourceBeforeEnvVars(
                configuration,
                new JsonStreamConfigurationSource
                {
                    Stream =
                        new ReusableMemoryStream(jsonBytes)
                });

            Console.WriteLine(
                $"[Startup] Loaded {devices.Count} device config(s) " +
                $"for agent {agentId}.");
        }
        catch (RequestFailedException ex)
            when (ex.Status == 404)
        {
            Console.WriteLine(
                "[Startup] No device-config container/blobs found; " +
                "agent has zero devices.");
        }
        catch (Exception ex)
        {
            // The Agent now starts with no devices at all. Without this
            // being reported, Cloud sees a healthy agent that simply has
            // none - indistinguishable from one legitimately configured
            // that way - while ADR-103 surfaces it as "camera.capture
            // Failed: no camera devices are assigned", pointing the
            // operator at capability assignment rather than at the
            // storage failure that actually happened.
            ReportError(
                "[Startup] Failed to load device configs, " +
                $"continuing with zero devices: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // Device processing
    // ---------------------------------------------------------------------

    private static bool TryProcessDeviceBlob(
        byte[] deviceBytes,
        string blobNameForLogging,
        string agentId,
        string deviceId,
        List<string> loadErrors,
        byte[]? credentialEncryptionKey,
        out JsonObject? deviceObject)
    {
        deviceObject = null;

        JsonNode? deviceNode;

        try
        {
            deviceNode =
                JsonNode.Parse(deviceBytes);
        }
        catch (JsonException ex)
        {
            Console.WriteLine(
                $"[Startup] Device config blob " +
                $"{blobNameForLogging} is not valid JSON, " +
                $"skipping: {ex.Message}");

            return false;
        }

        if (deviceNode is not JsonObject deviceObjectRaw)
        {
            Console.WriteLine(
                $"[Startup] Device config blob " +
                $"{blobNameForLogging} is not a JSON object, skipping.");

            return false;
        }

        // Check ownership before decrypting.
        if (!string.Equals(
                deviceObjectRaw["OwningAgentId"]?
                    .GetValue<string>(),
                agentId,
                StringComparison.Ordinal))
        {
            return false;
        }

        // Decrypt remote credentials.
        if (credentialEncryptionKey != null)
        {
            CredentialCipher.DecryptInPlace(
                deviceObjectRaw,
                credentialEncryptionKey);
        }

        JsonObject flattened;

        try
        {
            flattened =
                DeviceConfigRuntimeAdapter.Adapt(
                    deviceObjectRaw);
        }
        catch (UnsupportedConfigurationSchemaException ex)
        {
            var fallback =
                TryLoadCachedDeviceConfig(deviceId);

            if (fallback != null)
            {
                Console.WriteLine(
                    $"[Startup] Device config blob " +
                    $"{blobNameForLogging}: {ex.Message} " +
                    "Continuing on cached last-known-good config.");

                loadErrors.Add(
                    $"{ex.Message} Continuing on cached " +
                    $"last-known-good config for device {deviceId}.");

                TryMergeLocalDeviceSecrets(fallback);

                deviceObject = fallback;

                return true;
            }

            loadErrors.Add(ex.Message);

            Console.WriteLine(
                $"[Startup] Device config blob " +
                $"{blobNameForLogging}: {ex.Message} Skipping.");

            return false;
        }

        TryMergeLocalDeviceSecrets(flattened);

        TryWriteDeviceConfigCache(
            deviceId,
            deviceObjectRaw);

        deviceObject = flattened;

        return true;
    }

    // ---------------------------------------------------------------------
    // Device configuration cache
    // ---------------------------------------------------------------------

    private static string DeviceConfigCachePath(
        string deviceId)
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "config-cache",
            "devices",
            $"{deviceId}.json");
    }

    private static void TryWriteDeviceConfigCache(
        string deviceId,
        JsonObject rawDeviceDocument)
    {
        try
        {
            var cachePath =
                DeviceConfigCachePath(deviceId);

            Directory.CreateDirectory(
                Path.GetDirectoryName(cachePath)!);

            File.WriteAllBytes(
                cachePath,
                JsonSerializer.SerializeToUtf8Bytes(
                    rawDeviceDocument));
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Startup] Device {deviceId}: failed to write " +
                $"last-known-good config cache: {ex.Message}");
        }
    }

    private static JsonObject? TryLoadCachedDeviceConfig(
        string deviceId)
    {
        var cachePath =
            DeviceConfigCachePath(deviceId);

        if (!File.Exists(cachePath))
        {
            return null;
        }

        byte[] cachedBytes;

        try
        {
            cachedBytes =
                File.ReadAllBytes(cachePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Startup] Device {deviceId}: cached fallback " +
                $"config unreadable, dropping device: {ex.Message}");

            return null;
        }

        JsonNode? cachedNode;

        try
        {
            cachedNode =
                JsonNode.Parse(cachedBytes);
        }
        catch (JsonException ex)
        {
            Console.WriteLine(
                $"[Startup] Device {deviceId}: cached fallback " +
                $"config is not valid JSON, dropping device: {ex.Message}");

            return null;
        }

        if (cachedNode is not JsonObject cachedRaw)
        {
            return null;
        }

        try
        {
            return DeviceConfigRuntimeAdapter.Adapt(
                cachedRaw);
        }
        catch (UnsupportedConfigurationSchemaException ex)
        {
            Console.WriteLine(
                $"[Startup] Device {deviceId}: cached fallback " +
                $"config also unsupported ({ex.Message}), " +
                "dropping device.");

            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Device secrets
    // ---------------------------------------------------------------------

    private static void TryMergeLocalDeviceSecrets(
        JsonObject deviceObject)
    {
        var deviceId =
            deviceObject["DeviceId"]?
                .GetValue<string>();

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Console.WriteLine(
                "[Startup] Device config object has no DeviceId; " +
                "skipping local secrets merge.");

            return;
        }

        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "device-config",
                $"{deviceId}.secrets.json");

        if (!File.Exists(path))
        {
            Console.WriteLine(
                $"[Startup] No local secrets file found for device " +
                $"{deviceId} at {path}; continuing without them.");

            return;
        }

        try
        {
            var secretsBytes =
                File.ReadAllBytes(path);

            var secretsNode =
                JsonNode.Parse(secretsBytes);

            if (secretsNode is not JsonObject secretsObject)
            {
                Console.WriteLine(
                    $"[Startup] Device secrets file {path} " +
                    "is not a JSON object, skipping.");

                return;
            }

            MergeJsonInto(
                deviceObject,
                secretsObject);

            Console.WriteLine(
                $"[Startup] Merged local secrets for device " +
                $"{deviceId} from {path}.");
        }
        catch (Exception ex)
        {
            ReportError(
                $"[Startup] Failed to load local secrets for device " +
                $"{deviceId} from {path}, continuing without them: " +
                $"{ex.Message}");
        }
    }

    private static void MergeJsonInto(
        JsonObject target,
        JsonObject source)
    {
        foreach (var (key, sourceValue) in source)
        {
            if (sourceValue is JsonObject sourceObject &&
                target[key] is JsonObject targetObject)
            {
                MergeJsonInto(
                    targetObject,
                    sourceObject);
            }
            else
            {
                target[key] =
                    sourceValue?.DeepClone();
            }
        }
    }

    // ---------------------------------------------------------------------
    // Local shared configuration
    // ---------------------------------------------------------------------

    private static void TryLoadLocalSharedConfig(
        ConfigurationManager configuration,
        byte[]? credentialEncryptionKey)
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                SharedConfigBlob.BlobName);

        if (!File.Exists(path))
        {
            Console.WriteLine(
                $"[Startup] LoadLocalSettings is true but no local " +
                $"shared config file found at {path}; skipping.");

            return;
        }

        try
        {
            var configBytes =
                File.ReadAllBytes(path);

            configBytes =
                DecryptConfigBytes(
                    configBytes,
                    credentialEncryptionKey);

            InsertConfigSourceBeforeEnvVars(
                configuration,
                new JsonStreamConfigurationSource
                {
                    Stream =
                        new ReusableMemoryStream(configBytes)
                });

            Console.WriteLine(
                $"[Startup] Loaded local shared config from {path}.");
        }
        catch (Exception ex)
        {
            ReportError(
                $"[Startup] Failed to load local shared config from " +
                $"{path}, continuing without it: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // Local agent configuration
    // ---------------------------------------------------------------------

    private static void TryLoadLocalConfig(
        ConfigurationManager configuration,
        byte[]? credentialEncryptionKey)
    {
        var agentId =
            configuration["Agent:AgentId"];

        if (string.IsNullOrWhiteSpace(agentId))
        {
            Console.WriteLine(
                "[Startup] LoadLocalSettings is true but " +
                "Agent:AgentId is not set; skipping local config load.");

            return;
        }

        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                $"{agentId}.json");

        if (!File.Exists(path))
        {
            Console.WriteLine(
                $"[Startup] LoadLocalSettings is true but no local " +
                $"config file found at {path}; using local " +
                "appsettings only.");

            return;
        }

        try
        {
            var configBytes =
                File.ReadAllBytes(path);

            configBytes =
                DecryptConfigBytes(
                    configBytes,
                    credentialEncryptionKey);

            InsertConfigSourceBeforeEnvVars(
                configuration,
                new JsonStreamConfigurationSource
                {
                    Stream =
                        new ReusableMemoryStream(configBytes)
                });

            Console.WriteLine(
                $"[Startup] Loaded local config for agent " +
                $"{agentId} from {path}.");
        }
        catch (Exception ex)
        {
            ReportError(
                $"[Startup] Failed to load local config for agent " +
                $"{agentId} from {path}, continuing with local " +
                $"appsettings only: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // Local shared secrets
    // ---------------------------------------------------------------------

    private static void TryLoadLocalSharedSecrets(
        ConfigurationManager configuration)
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "common-config.secrets.json");

        if (!File.Exists(path))
        {
            Console.WriteLine(
                "[Startup] No local shared secrets file found; " +
                "continuing without it.");

            return;
        }

        try
        {
            var secretsBytes =
                File.ReadAllBytes(path);

            InsertConfigSourceBeforeEnvVars(
                configuration,
                new JsonStreamConfigurationSource
                {
                    Stream =
                        new ReusableMemoryStream(secretsBytes)
                });

            Console.WriteLine(
                "[Startup] Loaded local shared secrets.");
        }
        catch (Exception ex)
        {
            ReportError(
                "[Startup] Failed to load local shared secrets, " +
                $"continuing without them: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // Local agent secrets
    // ---------------------------------------------------------------------

    private static void TryLoadLocalAgentSecrets(
        ConfigurationManager configuration,
        string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            Console.WriteLine(
                "[Startup] Agent:AgentId is not set; " +
                "skipping local agent secrets load.");

            return;
        }

        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                $"{agentId}.secrets.json");

        if (!File.Exists(path))
        {
            Console.WriteLine(
                $"[Startup] No local secrets file found for agent " +
                $"{agentId}; continuing without them.");

            return;
        }

        try
        {
            var secretsBytes =
                File.ReadAllBytes(path);

            InsertConfigSourceBeforeEnvVars(
                configuration,
                new JsonStreamConfigurationSource
                {
                    Stream =
                        new ReusableMemoryStream(secretsBytes)
                });

            Console.WriteLine(
                $"[Startup] Loaded local secrets for agent " +
                $"{agentId}.");
        }
        catch (Exception ex)
        {
            ReportError(
                $"[Startup] Failed to load local secrets for agent " +
                $"{agentId}, continuing without them: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // Credential encryption
    // ---------------------------------------------------------------------

    private static byte[]? TryParseCredentialEncryptionKey(
        ConfigurationManager configuration)
    {
        var rawKey =
            configuration["CredentialEncryption:Key"];

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            Console.WriteLine(
                "[Startup] CredentialEncryption:Key is not set; " +
                "encrypted config fields will not be decrypted.");

            return null;
        }

        try
        {
            return CredentialCipher.ParseKey(rawKey);
        }
        catch (Exception ex)
        {
            ReportError(
                "[Startup] CredentialEncryption:Key is invalid, " +
                "encrypted config fields will not be decrypted: " +
                $"{ex.Message}");

            return null;
        }
    }

    private static byte[] DecryptConfigBytes(
        byte[] configBytes,
        byte[]? credentialEncryptionKey)
    {
        if (credentialEncryptionKey == null)
        {
            // The sibling path below - decryption attempted and failed -
            // already warned about this. The no-key path did not, even
            // though it is the likelier misconfiguration: an unset
            // environment variable rather than a wrong one.
            //
            // Silence here is expensive. An RTSP password stays the
            // literal "enc:v1:..." string, the camera refuses the
            // credentials, and the failure surfaces as an authentication
            // problem with the camera - nowhere near the missing key that
            // caused it.
            if (ContainsEncryptedValues(StripUtf8Bom(configBytes)))
            {
                ReportError(
                    "[Startup] WARNING: config contains enc:v1: values but " +
                    "CredentialEncryption:Key is not set, so they are being " +
                    "used UNDECRYPTED. Expect failures that look unrelated.");
            }

            return configBytes;
        }

        var json =
            StripUtf8Bom(configBytes);

        try
        {
            var node =
                JsonNode.Parse(json);

            CredentialCipher.DecryptInPlace(
                node,
                credentialEncryptionKey);

            return JsonSerializer.SerializeToUtf8Bytes(
                node);
        }
        catch (Exception ex)
        {
            ReportError(
                "[Startup] Failed to decrypt downloaded config, " +
                $"using it as-is: {ex.Message}");

            if (ContainsEncryptedValues(json))
            {
                ReportError(
                    "[Startup] WARNING: that config contains " +
                    "enc:v1: values which are now being used " +
                    "UNDECRYPTED. Expect failures that look " +
                    "unrelated.");
            }

            return json;
        }
    }

    private static byte[] StripUtf8Bom(
        byte[] bytes)
    {
        return bytes.Length >= 3 &&
               bytes[0] == 0xEF &&
               bytes[1] == 0xBB &&
               bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;
    }

    private static bool ContainsEncryptedValues(
        byte[] jsonBytes)
    {
        try
        {
            return Encoding.UTF8
                .GetString(jsonBytes)
                .Contains(
                    "enc:v1:",
                    StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------------
    // Configuration source ordering
    // ---------------------------------------------------------------------

    private static void InsertConfigSourceBeforeEnvVars(
        ConfigurationManager configuration,
        IConfigurationSource source)
    {
        var sources =
            configuration.Sources;

        var envVarsSourceIndex = -1;

        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is
                EnvironmentVariablesConfigurationSource)
            {
                envVarsSourceIndex = i;
                break;
            }
        }

        if (envVarsSourceIndex >= 0)
        {
            sources.Insert(
                envVarsSourceIndex,
                source);
        }
        else
        {
            sources.Add(source);
        }
    }

    // ---------------------------------------------------------------------
    // Reusable stream
    // ---------------------------------------------------------------------

    private sealed class ReusableMemoryStream : MemoryStream
    {
        public ReusableMemoryStream(byte[] buffer)
            : base(buffer)
        {
        }

        protected override void Dispose(
            bool disposing)
        {
            Position = 0;
        }
    }
}