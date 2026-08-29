using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Vivnest.Core.Constants;
using Vivnest.Core.ModelRegistry;
using Vivnest.Core.Storage;

namespace Vivnest.Capabilities.AiClassification.ModelProvisioning;

public interface IModelProvisioner
{
    // Resolves a per-device model config to the local path the inference
    // session should open. Registry reference (ModelId set) -> ensure the
    // version's file set is in the local cache (download + SHA-256
    // verify) and return the cached primary .onnx path; no ModelId ->
    // the legacy ModelPath, untouched. Null means the model is not
    // usable right now (reason already logged at Error, which
    // ErrorEventWorker ships as an AgentEvent) - the caller skips this
    // classification, and the NEXT one retries: fetch failures are never
    // cached (ADR-124; the restart-to-recover trap from the first
    // High-agent install).
    Task<string?> EnsureModelAsync(
        string modelId,
        string modelVersion,
        string modelFilesJson,
        string legacyModelPath,
        CancellationToken cancellationToken = default);
}

// Lazy, on first classify per (modelId, version) - never at startup, so
// one model's fetch problem cannot delay the host or the other model.
// Cache lives inside the container (ephemeral - a redeploy re-downloads
// ~20 MB on first use, accepted by design; see
// docs/architecture/model-registry-design.md).
public sealed class ModelProvisioner : IModelProvisioner
{
    private readonly IBlobStorageClient _blobClient;
    private readonly ILogger<ModelProvisioner> _logger;
    private readonly string _cacheRoot;

    // Only SUCCESSFUL ensures are remembered - a failed ensure must be
    // retried on the next message, never cached.
    private readonly ConcurrentDictionary<string, string> _ensured = new();

    public ModelProvisioner(IBlobStorageClient blobClient, ILogger<ModelProvisioner> logger)
        : this(blobClient, logger, Path.Combine(AppContext.BaseDirectory, "models-cache"))
    {
    }

    // Test seam: everything else identical, cache rooted somewhere
    // disposable.
    public ModelProvisioner(IBlobStorageClient blobClient, ILogger<ModelProvisioner> logger, string cacheRoot)
    {
        _blobClient = blobClient;
        _logger = logger;
        _cacheRoot = cacheRoot;
    }

    public async Task<string?> EnsureModelAsync(
        string modelId,
        string modelVersion,
        string modelFilesJson,
        string legacyModelPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return string.IsNullOrWhiteSpace(legacyModelPath) ? null : legacyModelPath;

        var cacheKey = $"{modelId}|{modelVersion}";

        if (_ensured.TryGetValue(cacheKey, out var known))
            return known;

        if (!int.TryParse(modelVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            _logger.LogError(
                "Model {ModelId}: published ModelVersion \"{ModelVersion}\" is not a number; cannot fetch.",
                modelId, modelVersion);

            return null;
        }

        var files = ModelFileEntry.TryDeserialize(modelFilesJson);

        if (files is not { Count: > 0 })
        {
            _logger.LogError(
                "Model {ModelId} v{Version}: published ModelFiles manifest is missing or malformed; cannot fetch.",
                modelId, version);

            return null;
        }

        var primary = files.FirstOrDefault(f => f.IsPrimary);

        if (primary == null)
        {
            _logger.LogError(
                "Model {ModelId} v{Version}: manifest has no primary file; cannot fetch.",
                modelId, version);

            return null;
        }

        var versionDir = Path.Combine(_cacheRoot, modelId, $"v{version}");

        try
        {
            Directory.CreateDirectory(versionDir);

            foreach (var file in files)
            {
                if (!await EnsureFileAsync(modelId, version, versionDir, file, cancellationToken))
                    return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Model {ModelId} v{Version}: fetch failed; will retry on the next classification.",
                modelId, version);

            return null;
        }

        var primaryPath = Path.Combine(versionDir, primary.Name);

        _ensured[cacheKey] = primaryPath;

        _logger.LogInformation(
            "Model {ModelId} v{Version} ready in local cache ({FileCount} file(s), primary {Primary}).",
            modelId, version, files.Count, primary.Name);

        return primaryPath;
    }

    private async Task<bool> EnsureFileAsync(
        string modelId,
        int version,
        string versionDir,
        ModelFileEntry file,
        CancellationToken cancellationToken)
    {
        var localPath = Path.Combine(versionDir, file.Name);

        if (File.Exists(localPath) && await HashMatchesAsync(localPath, file.Sha256, cancellationToken))
            return true;

        var bytes = await _blobClient.DownloadAsync(
            ModelBlob.ContainerName,
            ModelBlob.BlobName(modelId, version, file.Name),
            cancellationToken);

        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Model {ModelId} v{Version}: downloaded \"{File}\" hashes to {Actual}, manifest says {Expected} - " +
                "refusing to use it; will retry on the next classification.",
                modelId, version, file.Name, actualHash, file.Sha256);

            return false;
        }

        // Write-then-rename so a crash mid-write never leaves a
        // plausible-looking partial file that happens to pass File.Exists.
        var tempPath = localPath + ".downloading";

        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
        File.Move(tempPath, localPath, overwrite: true);

        return true;
    }

    private static async Task<bool> HashMatchesAsync(
        string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();

        return string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}
