using System.Security.Cryptography;
using Azure.Storage.Blobs.Models;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Entities;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.ModelRegistry;
using Vivnest.Core.Storage;

namespace Vivnest.Tests;

// Model registry (ADR-124): the semantics the design doc pins - immutable
// numbered versions, exactly one primary .onnx per file set, server-side
// SHA-256 as the authority, publish-time resolution (pin or latest
// Active) - verified against in-memory stores and a dict-backed blob
// client.
public class ModelRegistryTests
{
    private static readonly TenantContext Tenant = new("tenant-1", "site-1", DevicesOnly: false);

    // ---------------- registry service ----------------

    [Fact]
    public async Task UploadAssignsVersionNumbersAndHashesEveryFile()
    {
        var (service, blobs, _, _) = Build();
        var model = await service.CreateAsync(Tenant, "Sink Classifier", null);

        var onnx = new byte[] { 1, 2, 3 };
        var data = new byte[] { 4, 5, 6, 7 };

        var v1 = await service.CreateVersionAsync(Tenant, model.ModelId.ToString(),
            [new ModelFileUpload("m.onnx", onnx), new ModelFileUpload("m.onnx.data", data)], "first");

        Assert.Null(v1.Error);
        Assert.Equal(1, v1.Version!.Version);

        var v2 = await service.CreateVersionAsync(Tenant, model.ModelId.ToString(),
            [new ModelFileUpload("m.onnx", data)], null);

        Assert.Equal(2, v2.Version!.Version);

        var primary = v1.Version.Files.Single(f => f.IsPrimary);
        Assert.Equal("m.onnx", primary.Name);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(onnx)).ToLowerInvariant(), primary.Sha256);
        Assert.Equal(onnx, blobs.Blobs[$"models/{model.ModelId}/v1/m.onnx"]);
        Assert.Equal(data, blobs.Blobs[$"models/{model.ModelId}/v1/m.onnx.data"]);
    }

    [Fact]
    public async Task UploadRequiresExactlyOnePrimaryOnnx()
    {
        var (service, _, _, _) = Build();
        var model = await service.CreateAsync(Tenant, "M", null);

        var none = await service.CreateVersionAsync(Tenant, model.ModelId.ToString(),
            [new ModelFileUpload("weights.bin", [1])], null);
        var two = await service.CreateVersionAsync(Tenant, model.ModelId.ToString(),
            [new ModelFileUpload("a.onnx", [1]), new ModelFileUpload("b.onnx", [2])], null);

        Assert.Contains("exactly one .onnx", none.Error);
        Assert.Contains("exactly one .onnx", two.Error);
    }

    // ---------------- publish-time resolution ----------------

    [Fact]
    public async Task ResolverPassesThroughSettingsWithoutModelId()
    {
        var (_, _, models, versions) = Build();
        var resolver = new ModelReferenceResolver(models, versions);

        var settings = new Dictionary<string, string> { ["ModelPath"] = "/legacy.onnx" };
        var result = await resolver.ResolveAsync("tenant-1", "site-1", settings);

        Assert.Same(settings, result.Settings);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ResolverPicksLatestActiveVersionAndWritesTheManifest()
    {
        var (service, _, models, versions) = Build();
        var model = await service.CreateAsync(Tenant, "M", null);
        var id = model.ModelId.ToString();

        await service.CreateVersionAsync(Tenant, id, [new ModelFileUpload("a.onnx", [1])], null);
        await service.CreateVersionAsync(Tenant, id, [new ModelFileUpload("b.onnx", [2])], null);
        await service.CreateVersionAsync(Tenant, id, [new ModelFileUpload("c.onnx", [3])], null);
        // Retiring the newest makes v2 the latest ACTIVE.
        await service.UpdateVersionAsync(Tenant, id, 3, "Retired", null);

        var resolver = new ModelReferenceResolver(models, versions);
        var result = await resolver.ResolveAsync(
            "tenant-1", "site-1", new Dictionary<string, string> { ["ModelId"] = id });

        Assert.Empty(result.Warnings);
        Assert.Equal("2", result.Settings["ModelVersion"]);
        Assert.Equal("b.onnx", ModelFileEntry.TryDeserialize(result.Settings["ModelFiles"])!.Single().Name);
    }

    [Fact]
    public async Task ResolverHonorsAnExplicitPinEvenWhenRetired()
    {
        var (service, _, models, versions) = Build();
        var model = await service.CreateAsync(Tenant, "M", null);
        var id = model.ModelId.ToString();

        await service.CreateVersionAsync(Tenant, id, [new ModelFileUpload("a.onnx", [1])], null);
        await service.CreateVersionAsync(Tenant, id, [new ModelFileUpload("b.onnx", [2])], null);
        await service.UpdateVersionAsync(Tenant, id, 1, "Retired", null);

        var resolver = new ModelReferenceResolver(models, versions);
        var result = await resolver.ResolveAsync(
            "tenant-1", "site-1",
            new Dictionary<string, string> { ["ModelId"] = id, ["ModelVersion"] = "1" });

        Assert.Empty(result.Warnings);
        Assert.Equal("1", result.Settings["ModelVersion"]);
    }

    [Fact]
    public async Task ResolverWarnsOnDanglingReferences()
    {
        var (service, _, models, versions) = Build();
        var resolver = new ModelReferenceResolver(models, versions);

        var unknown = await resolver.ResolveAsync(
            "tenant-1", "site-1", new Dictionary<string, string> { ["ModelId"] = Guid.NewGuid().ToString() });
        Assert.Contains("doesn't exist", unknown.Warnings.Single());

        var model = await service.CreateAsync(Tenant, "Empty", null);
        var noVersion = await resolver.ResolveAsync(
            "tenant-1", "site-1", new Dictionary<string, string> { ["ModelId"] = model.ModelId.ToString() });
        Assert.Contains("no Active version", noVersion.Warnings.Single());
    }

    // ---------------- disabled assignments (the pause button) ----------------

    [Fact]
    public void DisabledRoiAssignmentPublishesInertWithNoValidation()
    {
        var projector = new Vivnest.Cloud.Admin.CapabilityProjection.ObjectDetectionRuntimeProjector();

        // No model, no ConfidenceThreshold, partial ROI - everything that
        // would block an ENABLED assignment. Disabled must publish inert.
        var assignment = new DeviceCapabilityEntity
        {
            PartitionKey = "t|s", RowKey = "a1", TenantId = "t", SiteId = "s",
            DeviceId = "d1", CapabilityId = "c1", Status = "Active", Enabled = false,
            ExecutingAgentId = "agent-1",
            Settings = "{\"RoiLeft\":\"1\"}",
        };

        var device = new DeviceRegistryEntity
        {
            PartitionKey = "t|s", RowKey = "d1", TenantId = "t", SiteId = "s",
            Name = "Cam", RuntimeDeviceId = "cam-01",
        };

        var result = projector.Project(assignment, device, "runtime-agent-1");

        Assert.Empty(result.Warnings);
        Assert.NotNull(result.DeviceEntry);
        Assert.False(result.DeviceEntry!.Enabled);
        Assert.Null(result.AgentEntry);
    }

    // ---------------- shared fixtures ----------------

    private static (ModelRegistryService Service, DictBlobClient Blobs, FakeModelStore Models, FakeModelVersionStore Versions) Build()
    {
        var blobs = new DictBlobClient();
        var models = new FakeModelStore();
        var versions = new FakeModelVersionStore();

        return (new ModelRegistryService(models, versions, blobs), blobs, models, versions);
    }

    internal sealed class FakeModelStore : IModelStore
    {
        public List<ModelEntity> Rows { get; } = [];

        public Task<IReadOnlyList<ModelEntity>> ListAsync(string t, string s, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ModelEntity>>(
                Rows.Where(r => r.PartitionKey == $"{t}|{s}").ToList());

        public Task<ModelEntity?> GetAsync(string t, string s, string modelId, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.PartitionKey == $"{t}|{s}" && r.RowKey == modelId));

        public Task CreateAsync(ModelEntity entity, CancellationToken ct = default)
        {
            Rows.Add(entity);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ModelEntity entity, CancellationToken ct = default) => Task.CompletedTask;
    }

    internal sealed class FakeModelVersionStore : IModelVersionStore
    {
        public List<ModelVersionEntity> Rows { get; } = [];

        public Task<IReadOnlyList<ModelVersionEntity>> ListAsync(
            string t, string s, string modelId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ModelVersionEntity>>(
                Rows.Where(r => r.PartitionKey == $"{t}|{s}|{modelId}").ToList());

        public Task<ModelVersionEntity?> GetAsync(
            string t, string s, string modelId, int version, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(r =>
                r.PartitionKey == $"{t}|{s}|{modelId}" && r.Version == version));

        public Task CreateAsync(ModelVersionEntity entity, CancellationToken ct = default)
        {
            Rows.Add(entity);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ModelVersionEntity entity, CancellationToken ct = default) => Task.CompletedTask;
    }

    // Dict-backed blob client - upload stores, download serves, so the
    // provisioner tests can round-trip real bytes through it.
    internal sealed class DictBlobClient : IBlobStorageClient
    {
        public Dictionary<string, byte[]> Blobs { get; } = [];

        public async Task UploadAsync(
            string containerName, string blobName, Stream content,
            BlobHttpHeaders? httpHeaders = null, bool failIfExists = false,
            CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            Blobs[$"{containerName}/{blobName}"] = memory.ToArray();
        }

        public Task<byte[]> DownloadAsync(
            string containerName, string blobName, CancellationToken cancellationToken = default) =>
            Blobs.TryGetValue($"{containerName}/{blobName}", out var bytes)
                ? Task.FromResult(bytes)
                : throw new FileNotFoundException($"{containerName}/{blobName}");

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
