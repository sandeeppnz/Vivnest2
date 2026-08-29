using System.Text;
using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.Configuration;
using Vivnest.Domain.Devices;

namespace Vivnest.Cloud.Admin.Seeding;

// Reconciles the stored catalogue against CatalogueSeed (ADR-119).
// Composes the existing management services rather than raw stores, so
// every write goes through the same validation and no-op behavior the
// admin screens use. Idempotent: a second run reports everything
// Unchanged.
public sealed class CatalogueSeedService : ICatalogueSeedService
{
    private readonly ICapabilityManagementService _capabilities;
    private readonly IDeviceTypeManagementService _deviceTypes;
    private readonly ICapabilityCompatibilityService _compatibility;
    private readonly IEnumerable<ICapabilityRuntimeProjector> _projectors;

    public CatalogueSeedService(
        ICapabilityManagementService capabilities,
        IDeviceTypeManagementService deviceTypes,
        ICapabilityCompatibilityService compatibility,
        IEnumerable<ICapabilityRuntimeProjector> projectors)
    {
        _capabilities = capabilities;
        _deviceTypes = deviceTypes;
        _compatibility = compatibility;
        _projectors = projectors;
    }

    public async Task<CatalogueSeedReport> SeedAsync(CancellationToken cancellationToken = default)
    {
        var created = new List<string>();
        var repaired = new List<string>();
        var unchanged = new List<string>();
        var warnings = new List<string>();

        // Coverage check against the LIVE projector set, not a copy of
        // it: a projector added without a seed entry would otherwise be
        // exactly the hand-typed magic string this service exists to
        // eliminate. Warn, never throw - a partial seed is still better
        // than none.
        foreach (var projector in _projectors)
        {
            if (!CatalogueSeed.Capabilities.Any(s =>
                    NamesMatch(s.Name, projector.CapabilityName)))
            {
                warnings.Add(
                    $"projector \"{projector.CapabilityName}\" has no CatalogueSeed entry - " +
                    "its capability must still be created by hand");
            }
        }

        var deviceTypeIds = await SeedDeviceTypesAsync(created, unchanged, cancellationToken);
        var capabilityIds = await SeedCapabilitiesAsync(created, repaired, unchanged, cancellationToken);
        await SeedCompatibilityLinksAsync(deviceTypeIds, capabilityIds, created, unchanged, warnings, cancellationToken);

        return new CatalogueSeedReport(created, repaired, unchanged, warnings);
    }

    private async Task<IReadOnlyDictionary<DeviceType, string>> SeedDeviceTypesAsync(
        List<string> created,
        List<string> unchanged,
        CancellationToken cancellationToken)
    {
        var existing = await _deviceTypes.ListAsync(cancellationToken);
        var byRuntimeType = new Dictionary<DeviceType, string>();

        foreach (var row in existing)
        {
            var match = RuntimeNameMatch.ToDeviceType(row.DeviceTypeName);

            // First row wins on duplicates - same rule the projector's
            // own matching effectively applies.
            if (match != null && !byRuntimeType.ContainsKey(match.Value))
                byRuntimeType[match.Value] = row.DeviceTypeId.ToString();
        }

        foreach (var type in Enum.GetValues<DeviceType>())
        {
            if (byRuntimeType.ContainsKey(type))
            {
                unchanged.Add($"device type: {DisplayName(type)}");
                continue;
            }

            var row = await _deviceTypes.CreateAsync(
                DisplayName(type), description: null, cancellationToken);

            byRuntimeType[type] = row.DeviceTypeId.ToString();
            created.Add($"device type: {DisplayName(type)}");
        }

        return byRuntimeType;
    }

    private async Task<IReadOnlyDictionary<string, string>> SeedCapabilitiesAsync(
        List<string> created,
        List<string> repaired,
        List<string> unchanged,
        CancellationToken cancellationToken)
    {
        var existing = await _capabilities.ListAsync(cancellationToken);
        var idBySeedName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in CatalogueSeed.Capabilities)
        {
            // Key is the stronger identity (unique, runtime-bound); fall
            // back to name so a keyless hand-created row gets REPAIRED
            // rather than duplicated - the exact hole found live.
            var row =
                existing.FirstOrDefault(c =>
                    !string.IsNullOrWhiteSpace(c.CapabilityKey) &&
                    string.Equals(c.CapabilityKey, seed.Key, StringComparison.OrdinalIgnoreCase))
                ?? existing.FirstOrDefault(c => NamesMatch(c.CapabilityName, seed.Name));

            if (row == null)
            {
                var createdRow = await _capabilities.CreateAsync(
                    seed.Name, seed.Type, seed.Schema, 1, seed.Defaults, seed.Key, cancellationToken);

                idBySeedName[seed.Name] = createdRow.CapabilityId.ToString();
                created.Add($"capability: {seed.Name} ({seed.Key})");
                continue;
            }

            idBySeedName[seed.Name] = row.CapabilityId.ToString();

            var keyDrifted = !string.Equals(row.CapabilityKey, seed.Key, StringComparison.Ordinal);
            var nameDrifted = !string.Equals(row.CapabilityName, seed.Name, StringComparison.Ordinal);

            if (!keyDrifted && !nameDrifted)
            {
                unchanged.Add($"capability: {seed.Name}");
                continue;
            }

            // Repair identity only. Schema/defaults/status stay exactly
            // as stored - those are legitimately admin-tunable; the
            // name/key pair is not, because the runtime binds on it.
            await _capabilities.UpdateAsync(
                row.CapabilityId.ToString(),
                seed.Name,
                row.CapabilityType,
                row.Status,
                row.ConfigurationSchema,
                row.ConfigurationSchemaVersion,
                row.DefaultConfiguration,
                seed.Key,
                cancellationToken);

            repaired.Add(
                $"capability: {seed.Name} - " +
                (keyDrifted ? $"key set to {seed.Key}" : "name restored"));
        }

        return idBySeedName;
    }

    private async Task SeedCompatibilityLinksAsync(
        IReadOnlyDictionary<DeviceType, string> deviceTypeIds,
        IReadOnlyDictionary<string, string> capabilityIds,
        List<string> created,
        List<string> unchanged,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var existing = await _compatibility.ListAllAsync(cancellationToken);

        foreach (var seed in CatalogueSeed.Capabilities)
        {
            if (!capabilityIds.TryGetValue(seed.Name, out var capabilityId))
                continue;

            foreach (var type in seed.CompatibleDeviceTypes)
            {
                if (!deviceTypeIds.TryGetValue(type, out var deviceTypeId))
                {
                    warnings.Add($"link: {seed.Name} <-> {DisplayName(type)} skipped - device type missing");
                    continue;
                }

                var label = $"link: {DisplayName(type)} <-> {seed.Name}";

                if (existing.Any(l =>
                        l.DeviceTypeId.ToString() == deviceTypeId &&
                        l.CapabilityId.ToString() == capabilityId))
                {
                    unchanged.Add(label);
                    continue;
                }

                var result = await _compatibility.AddAsync(deviceTypeId, capabilityId, cancellationToken);

                if (result.Compatibility != null)
                    created.Add(label);
                else
                    warnings.Add($"{label} failed: {result.ErrorMessage}");
            }
        }
    }

    private static bool NamesMatch(string a, string b) =>
        string.Equals(Collapse(a), Collapse(b), StringComparison.OrdinalIgnoreCase);

    private static string Collapse(string value) =>
        value.Replace(" ", "", StringComparison.Ordinal);

    // "MotionSensor" -> "Motion Sensor", matching the free-text style the
    // admin screens have always used; RuntimeNameMatch maps it back.
    private static string DisplayName(DeviceType type)
    {
        var name = type.ToString();
        var sb = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                sb.Append(' ');
            sb.Append(name[i]);
        }

        return sb.ToString();
    }
}
