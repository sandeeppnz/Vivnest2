using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Models;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Storage;

namespace Vivnest.Tests;

// In-memory stand-ins for the two seams the configuration publishers depend
// on. Hand-written rather than mocked: the behaviours under test are things
// like "the second write to this blob name must 409" and "an Update with a
// stale ETag must 412", which are easier to state directly than to express
// through a mocking framework's setup calls.
public sealed class FakeBlobStorageClient : IBlobStorageClient
{
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public List<string> Uploads { get; } = [];

    public IReadOnlyDictionary<string, byte[]> Blobs => _blobs;

    public byte[]? Get(string container, string blobName) =>
        _blobs.TryGetValue(Key(container, blobName), out var bytes) ? bytes : null;

    public void Seed(string container, string blobName, byte[] content) =>
        _blobs[Key(container, blobName)] = content;

    private static string Key(string container, string blobName) => $"{container}/{blobName}";

    public Task UploadAsync(
        string containerName,
        string blobName,
        Stream content,
        BlobHttpHeaders? httpHeaders = null,
        bool failIfExists = false,
        CancellationToken cancellationToken = default)
    {
        var key = Key(containerName, blobName);

        // Mirrors Azure's conditional-create: failIfExists sets
        // IfNoneMatch "*", which 409s when the blob is already there. The
        // publishers rely on exactly this to detect a concurrent publish
        // claiming the same version number.
        if (failIfExists && _blobs.ContainsKey(key))
            throw new RequestFailedException(409, "BlobAlreadyExists");

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);

        _blobs[key] = buffer.ToArray();
        Uploads.Add(key);

        return Task.CompletedTask;
    }

    public Task<byte[]> DownloadAsync(
        string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var key = Key(containerName, blobName);

        if (!_blobs.TryGetValue(key, out var bytes))
            throw new RequestFailedException(404, "BlobNotFound");

        return Task.FromResult(bytes);
    }

    public Task<IReadOnlyList<string>> ListBlobNamesAsync(
        string containerName, CancellationToken cancellationToken = default)
    {
        var prefix = containerName + "/";

        IReadOnlyList<string> names = _blobs.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k[prefix.Length..])
            .ToList();

        return Task.FromResult(names);
    }

    public Task<Stream> OpenReadAsync(
        string containerName, string blobName, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(Get(containerName, blobName) ?? []));

    public Uri GenerateReadSasUri(
        string containerName, string blobName, TimeSpan validFor, string? cacheControl = null) =>
        new($"https://fake/{containerName}/{blobName}");
}

// One fake for both configuration tables - the rows are the same shape
// (IConfigurationStateEntity) and the shared publish pipeline is written
// once over both, so the fake it runs against should be too.
public abstract class FakeConfigurationStore<TEntity> : IConfigurationStateStore<TEntity>
    where TEntity : class, ITableEntity, IConfigurationStateEntity
{
    private readonly Dictionary<string, TEntity> _rows = new(StringComparer.Ordinal);

    // Set to force the next Update to behave as though another publisher
    // won the race - the 412 the retry loop is built around.
    public bool FailNextUpdateWithPreconditionFailed { get; set; }

    public int UpdateCalls { get; private set; }

    // How a competing publisher's row is rebuilt - entity-specific only
    // because TenantId/SiteId are `required init`, so the derived fake has
    // to new it up itself.
    protected abstract TEntity Advance(TEntity current);

    public Task<TEntity?> GetAsync(
        string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.TryGetValue($"{partitionKey}|{rowKey}", out var row) ? row : null);

    public Task UpsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        _rows[$"{entity.PartitionKey}|{entity.RowKey}"] = entity;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        UpdateCalls++;

        if (FailNextUpdateWithPreconditionFailed)
        {
            FailNextUpdateWithPreconditionFailed = false;

            // A 412 never happens in isolation: it means a competing
            // publisher already advanced this row, which is why our ETag
            // went stale. Simulating the throw *without* advancing the row
            // models a state Azure cannot produce - and it deadlocks the
            // retry, because the publisher re-reads the same
            // CurrentVersion, recomputes the same next version, and then
            // collides (409) with the version blob its own failed attempt
            // already wrote. The retry loop is built on the assumption
            // that a 412 comes with a moved row, so the fake has to honour
            // that to be a fair test.
            var key = $"{entity.PartitionKey}|{entity.RowKey}";

            if (_rows.TryGetValue(key, out var current))
                _rows[key] = Advance(current);

            throw new RequestFailedException(412, "ConditionNotMet");
        }

        _rows[$"{entity.PartitionKey}|{entity.RowKey}"] = entity;
        return Task.CompletedTask;
    }
}

public sealed class FakeAgentConfigurationStore
    : FakeConfigurationStore<AgentConfigurationEntity>, IAgentConfigurationStore
{
    protected override AgentConfigurationEntity Advance(AgentConfigurationEntity current) =>
        new()
        {
            PartitionKey = current.PartitionKey,
            RowKey = current.RowKey,
            TenantId = current.TenantId,
            SiteId = current.SiteId,
            CurrentVersion = current.CurrentVersion + 1,
            CurrentHash = "written-by-a-competing-publisher",
            PublishedUtc = DateTime.UtcNow,
            ETag = new ETag(Guid.NewGuid().ToString())
        };
}

public sealed class FakeDeviceConfigurationStore
    : FakeConfigurationStore<DeviceConfigurationEntity>, IDeviceConfigurationStore
{
    protected override DeviceConfigurationEntity Advance(DeviceConfigurationEntity current) =>
        new()
        {
            PartitionKey = current.PartitionKey,
            RowKey = current.RowKey,
            TenantId = current.TenantId,
            SiteId = current.SiteId,
            CurrentVersion = current.CurrentVersion + 1,
            CurrentHash = "written-by-a-competing-publisher",
            PublishedUtc = DateTime.UtcNow,
            ETag = new ETag(Guid.NewGuid().ToString())
        };
}

public sealed class FakeAgentEventStore : IAgentEventStore
{
    public List<AgentEventEntity> Written { get; } = [];

    public Task UpsertAsync(AgentEventEntity entity, CancellationToken cancellationToken = default)
    {
        Written.Add(entity);
        return Task.CompletedTask;
    }
}

public sealed class FakeDeviceEventStore : IDeviceEventStore
{
    public List<DeviceEventEntity> Written { get; } = [];

    public Task UpsertAsync(DeviceEventEntity entity, CancellationToken cancellationToken = default)
    {
        Written.Add(entity);
        return Task.CompletedTask;
    }
}
