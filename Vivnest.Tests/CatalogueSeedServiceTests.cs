using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Admin.Seeding;
using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Tests;

// Catalogue self-seeding (ADR-119). The one test that matters most is
// coverage: CatalogueSeed restates identity constants the runtime
// compiled in (projector names, adapter keys), and this file pins the
// manifest against the REAL projector classes - add a projector without
// a seed entry and the suite fails, instead of a future first-run
// setup failing silently the way the 2026-08-29 rebuild did.
public class CatalogueSeedServiceTests
{
    private static readonly IReadOnlyList<ICapabilityRuntimeProjector> RealProjectors =
    [
        new ImageCaptureRuntimeProjector(),
        new MotionDetectionRuntimeProjector(new EmptyDeviceTypeStore()),
        new ObjectDetectionRuntimeProjector(),
        new SinkCleanlinessRuntimeProjector(),
    ];

    [Fact]
    public void EveryProjectorHasASeedEntry()
    {
        foreach (var projector in RealProjectors)
        {
            Assert.Contains(CatalogueSeed.Capabilities, s =>
                string.Equals(
                    s.Name.Replace(" ", ""),
                    projector.CapabilityName.Replace(" ", ""),
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void SeedKeysAreUniqueAndDotted()
    {
        var keys = CatalogueSeed.Capabilities.Select(s => s.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(keys, k => Assert.Contains('.', k));
    }

    [Fact]
    public void ImageCaptureSeedMatchesTheAdapterKeyAndProjectorSchema()
    {
        var seed = CatalogueSeed.Capabilities.Single(s => s.Name == "Image Capture");

        // The adapter id CameraCapability declares - the value
        // AgentCapabilityAssignmentFactory filters on. A drift here is
        // the exact silent do-nothing failure found live.
        Assert.Equal("camera.capture", seed.Key);

        // ImageCaptureRuntimeProjector's three required setting keys.
        Assert.Equal(
            ["ScheduleIntervalSeconds", "BurstIntervalSeconds", "BurstDurationSeconds"],
            seed.Schema.Where(f => f.Required).Select(f => f.Name).ToArray());
    }

    [Fact]
    public async Task SeedingTwiceIsIdempotent()
    {
        var caps = new FakeCapabilityService();
        var types = new FakeDeviceTypeService();
        var links = new FakeCompatibilityService();
        var service = new CatalogueSeedService(caps, types, links, RealProjectors);

        var first = await service.SeedAsync();
        Assert.NotEmpty(first.Created);
        Assert.Empty(first.Warnings);

        var second = await service.SeedAsync();
        Assert.Empty(second.Created);
        Assert.Empty(second.Repaired);
        Assert.NotEmpty(second.Unchanged);
    }

    [Fact]
    public async Task KeylessCapabilityIsRepairedNotDuplicated()
    {
        var caps = new FakeCapabilityService();
        // The state the live rebuild produced: right name, no key.
        await caps.CreateAsync("Image Capture", "Device", null, null, null, capabilityKey: null);

        var service = new CatalogueSeedService(
            caps, new FakeDeviceTypeService(), new FakeCompatibilityService(), RealProjectors);

        var report = await service.SeedAsync();

        Assert.Contains(report.Repaired, r => r.Contains("camera.capture"));
        Assert.Single(caps.Rows, c => c.CapabilityName == "Image Capture");
        Assert.Equal("camera.capture",
            caps.Rows.Single(c => c.CapabilityName == "Image Capture").CapabilityKey);
    }

    // ---------------------------------------------------------------
    // Minimal in-memory fakes - just enough surface for the seeder.
    // ---------------------------------------------------------------

    // Only satisfies MotionDetectionRuntimeProjector's ctor - none of
    // these tests exercise its projection path.
    private sealed class EmptyDeviceTypeStore : Vivnest.Cloud.Interfaces.IDeviceTypeStore
    {
        public Task<IReadOnlyList<Vivnest.Cloud.Entities.DeviceTypeEntity>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Vivnest.Cloud.Entities.DeviceTypeEntity>>([]);

        public Task<Vivnest.Cloud.Entities.DeviceTypeEntity?> GetAsync(string deviceTypeId, CancellationToken ct = default) =>
            Task.FromResult<Vivnest.Cloud.Entities.DeviceTypeEntity?>(null);

        public Task CreateAsync(Vivnest.Cloud.Entities.DeviceTypeEntity entity, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(Vivnest.Cloud.Entities.DeviceTypeEntity entity, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string deviceTypeId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCapabilityService : ICapabilityManagementService
    {
        public List<CapabilityAdminDto> Rows { get; } = [];

        public Task<IReadOnlyList<CapabilityAdminDto>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CapabilityAdminDto>>(Rows.ToList());

        public Task<CapabilityAdminDto> CreateAsync(
            string capabilityName, string capabilityType,
            IReadOnlyList<CapabilityConfigurationFieldDto>? configurationSchema,
            int? configurationSchemaVersion,
            IReadOnlyDictionary<string, string>? defaultConfiguration,
            string? capabilityKey = null, CancellationToken ct = default)
        {
            var row = new CapabilityAdminDto(
                Guid.NewGuid(), capabilityKey, capabilityName, capabilityType, "Active",
                configurationSchema ?? [], configurationSchemaVersion ?? 1,
                defaultConfiguration ?? new Dictionary<string, string>());
            Rows.Add(row);
            return Task.FromResult(row);
        }

        public Task<CapabilityAdminDto?> UpdateAsync(
            string capabilityId, string capabilityName, string capabilityType, string status,
            IReadOnlyList<CapabilityConfigurationFieldDto>? configurationSchema,
            int? configurationSchemaVersion,
            IReadOnlyDictionary<string, string>? defaultConfiguration,
            string? capabilityKey = null, CancellationToken ct = default)
        {
            var index = Rows.FindIndex(r => r.CapabilityId.ToString() == capabilityId);
            if (index < 0) return Task.FromResult<CapabilityAdminDto?>(null);

            var updated = Rows[index] with
            {
                CapabilityName = capabilityName,
                CapabilityType = capabilityType,
                Status = status,
                CapabilityKey = capabilityKey ?? Rows[index].CapabilityKey,
            };
            Rows[index] = updated;
            return Task.FromResult<CapabilityAdminDto?>(updated);
        }

        public Task<CapabilityDeleteResult> DeleteAsync(string capabilityId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDeviceTypeService : IDeviceTypeManagementService
    {
        public List<DeviceTypeAdminDto> Rows { get; } = [];

        public Task<IReadOnlyList<DeviceTypeAdminDto>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceTypeAdminDto>>(Rows.ToList());

        public Task<DeviceTypeAdminDto> CreateAsync(
            string deviceTypeName, string? description, CancellationToken ct = default)
        {
            var row = new DeviceTypeAdminDto(
                Guid.NewGuid(), deviceTypeName, description, "Active",
                DateTime.UtcNow, DateTime.UtcNow);
            Rows.Add(row);
            return Task.FromResult(row);
        }

        public Task<DeviceTypeAdminDto?> UpdateAsync(
            string deviceTypeId, string deviceTypeName, string? description, string status,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(string deviceTypeId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCompatibilityService : ICapabilityCompatibilityService
    {
        public List<DeviceTypeCapabilityDto> Rows { get; } = [];

        public Task<IReadOnlyList<DeviceTypeCapabilityDto>> ListAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceTypeCapabilityDto>>(Rows.ToList());

        public Task<CapabilityCompatibilityResult> AddAsync(
            string deviceTypeId, string capabilityId, CancellationToken ct = default)
        {
            var row = new DeviceTypeCapabilityDto(
                Guid.NewGuid(), Guid.Parse(deviceTypeId), Guid.Parse(capabilityId));
            Rows.Add(row);
            return Task.FromResult(new CapabilityCompatibilityResult(row, null, null));
        }

        public Task<bool> RemoveAsync(string deviceTypeCapabilityId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
