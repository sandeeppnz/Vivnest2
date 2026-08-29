using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Vivnest.Capabilities.AiClassification.ModelProvisioning;
using Vivnest.Core.ModelRegistry;

namespace Vivnest.Tests;

// Agent-side model fetch (ADR-124): hash-verified download into a local
// cache, legacy passthrough, and - the trap from the first High-agent
// install - fetch failures NEVER cached, so the next attempt retries.
public class ModelProvisionerTests : IDisposable
{
    private readonly string _cacheRoot =
        Path.Combine(Path.GetTempPath(), "vivnest-model-tests", Guid.NewGuid().ToString("N"));

    private readonly ModelRegistryTests.DictBlobClient _blobs = new();

    private ModelProvisioner Provisioner =>
        new(_blobs, NullLogger<ModelProvisioner>.Instance, _cacheRoot);

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
            Directory.Delete(_cacheRoot, recursive: true);
    }

    [Fact]
    public async Task NoModelIdFallsBackToLegacyPath()
    {
        Assert.Equal("/legacy.onnx",
            await Provisioner.EnsureModelAsync("", "", "", "/legacy.onnx"));

        Assert.Null(await Provisioner.EnsureModelAsync("", "", "", ""));
    }

    [Fact]
    public async Task DownloadsVerifiesAndReturnsThePrimaryPath()
    {
        var modelId = Guid.NewGuid().ToString();
        var onnx = new byte[] { 1, 2, 3 };
        var data = new byte[] { 4, 5, 6 };

        _blobs.Blobs[$"models/{modelId}/v2/m.onnx"] = onnx;
        _blobs.Blobs[$"models/{modelId}/v2/m.onnx.data"] = data;

        var manifest = ModelFileEntry.Serialize(
        [
            new ModelFileEntry("m.onnx", 3, Hash(onnx), IsPrimary: true),
            new ModelFileEntry("m.onnx.data", 3, Hash(data), IsPrimary: false),
        ]);

        var path = await Provisioner.EnsureModelAsync(modelId, "2", manifest, "");

        Assert.NotNull(path);
        Assert.EndsWith("m.onnx", path);
        Assert.Equal(onnx, await File.ReadAllBytesAsync(path!));
        // The companion landed BESIDE the primary - the layout ONNX
        // Runtime's external-data resolution requires.
        Assert.Equal(data, await File.ReadAllBytesAsync(
            Path.Combine(Path.GetDirectoryName(path!)!, "m.onnx.data")));
    }

    [Fact]
    public async Task HashMismatchFailsButTheNextAttemptRetries()
    {
        var modelId = Guid.NewGuid().ToString();
        var good = new byte[] { 9, 9, 9 };

        _blobs.Blobs[$"models/{modelId}/v1/m.onnx"] = new byte[] { 1 };

        var manifest = ModelFileEntry.Serialize(
            [new ModelFileEntry("m.onnx", 3, Hash(good), IsPrimary: true)]);

        var provisioner = Provisioner;

        Assert.Null(await provisioner.EnsureModelAsync(modelId, "1", manifest, ""));

        // The blob is fixed (e.g. re-uploaded) - the SAME provisioner
        // instance must retry rather than remember the failure.
        _blobs.Blobs[$"models/{modelId}/v1/m.onnx"] = good;

        Assert.NotNull(await provisioner.EnsureModelAsync(modelId, "1", manifest, ""));
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
