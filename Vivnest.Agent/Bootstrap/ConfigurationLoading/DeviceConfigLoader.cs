using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vivnest.Core.Configuration;
using Vivnest.Core.Constants;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Agent.Bootstrap.ConfigurationLoading;

// Builds the "Devices" configuration section for a Low agent: lists the
// device-config container, reads each device document (versioned
// manifests first, then legacy flat blobs), filters to the ones this
// agent owns, decrypts and flattens them through
// DeviceConfigRuntimeAdapter, overlays local per-device secrets, and
// keeps a last-known-good cache beside the executable so a device whose
// remote document turns unsupported keeps running on its previous one.
//
// Per-device failures skip that device and are reported; a whole-load
// failure leaves the agent with zero devices - also reported, because
// without that Cloud sees a healthy agent that simply has none,
// indistinguishable from one legitimately configured that way.
internal sealed class DeviceConfigLoader
{
    private readonly ConfigurationManager _configuration;
    private readonly ConfigDecryptor _decryptor;
    private readonly StartupErrorSink _errors;

    public DeviceConfigLoader(
        ConfigurationManager configuration,
        ConfigDecryptor decryptor,
        StartupErrorSink errors)
    {
        _configuration = configuration;
        _decryptor = decryptor;
        _errors = errors;
    }

    public async Task TryLoadAsync(
        string agentId)
    {
        var storageConnectionString =
            _configuration["Storage:ConnectionString"];

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
                    _configuration["Agent:TenantId"] ?? "",
                    _configuration["Agent:SiteId"] ?? "");

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
                    _errors.Report(
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
                    _errors.Report(
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
                    _errors.Report(
                        $"[Startup] Failed to download device config " +
                        $"blob {blobName}, skipping: {ex.Message}");

                    continue;
                }

                if (TryProcessDeviceBlob(
                    deviceBytes,
                    blobName,
                    agentId,
                    deviceId,
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

            ConfigSourceInsertion.InsertJsonBeforeEnvVars(
                _configuration,
                jsonBytes);

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
            _errors.Report(
                "[Startup] Failed to load device configs, " +
                $"continuing with zero devices: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // Device processing
    // ---------------------------------------------------------------------

    private bool TryProcessDeviceBlob(
        byte[] deviceBytes,
        string blobNameForLogging,
        string agentId,
        string deviceId,
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
        _decryptor.TryDecryptInPlace(deviceObjectRaw);

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

                _errors.Add(
                    $"{ex.Message} Continuing on cached " +
                    $"last-known-good config for device {deviceId}.");

                TryMergeLocalDeviceSecrets(fallback);

                deviceObject = fallback;

                return true;
            }

            _errors.Add(ex.Message);

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
    // Last-known-good cache
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

    private void TryMergeLocalDeviceSecrets(
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
            _errors.Report(
                $"[Startup] Failed to load local secrets for device " +
                $"{deviceId} from {path}, continuing without them: " +
                $"{ex.Message}");
        }
    }

    // Deep merge: nested objects merge key-by-key, everything else (values,
    // arrays) is replaced by the source's clone. The secrets overlay relies
    // on this - a secrets file carrying only {"Camera":{"Password":...}}
    // must not erase the Camera object's other properties.
    internal static void MergeJsonInto(
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
}
