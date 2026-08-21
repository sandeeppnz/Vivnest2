using Vivnest.Core.Enums;

namespace Vivnest.Abstractions.Domain;

// Admin > Capabilities master list (decision-log.md ADR-042/057/062) - a
// reusable capability definition ("Image Capture", "Object Detection").
// Global, not tenant-scoped - shared reference data, same reasoning as
// DeviceType. ADR-062 (Phase 5) added ConfigurationSchema/
// ConfigurationSchemaVersion/DefaultConfiguration (what configuration
// this capability needs and what it defaults to - CapabilityConfigurationService
// validates DeviceCapability.Settings against this) and Status (Active/
// Retired - CapabilityManagementService.DeleteAsync rejects a hard
// delete once a CapabilityDependency/DeviceTypeCapability references
// this Capability; retiring is the alternative).
public sealed class Capability
{
    private static readonly IReadOnlyList<CapabilityConfigurationField> EmptySchema =
        Array.Empty<CapabilityConfigurationField>();

    private static readonly IReadOnlyDictionary<string, string> EmptyDefaults =
        new Dictionary<string, string>();

    public string CapabilityId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public CapabilityType CapabilityType { get; private set; }

    public CapabilityStatus Status { get; private set; }

    public IReadOnlyList<CapabilityConfigurationField> ConfigurationSchema { get; private set; } =
        EmptySchema;

    public int ConfigurationSchemaVersion { get; private set; } = 1;

    public IReadOnlyDictionary<string, string> DefaultConfiguration { get; private set; } =
        EmptyDefaults;

    private Capability()
    {
    }

    public Capability(
        string name,
        CapabilityType capabilityType,
        IReadOnlyList<CapabilityConfigurationField>? configurationSchema = null,
        int configurationSchemaVersion = 1,
        IReadOnlyDictionary<string, string>? defaultConfiguration = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        CapabilityId = Guid.NewGuid().ToString();
        Name = name;
        CapabilityType = capabilityType;
        Status = CapabilityStatus.Active;
        ConfigurationSchema = configurationSchema ?? EmptySchema;
        ConfigurationSchemaVersion = configurationSchemaVersion;
        DefaultConfiguration = defaultConfiguration ?? EmptyDefaults;
    }

    // Rehydrates from storage - see Tenant.Rehydrate for why this bypasses
    // the validating constructor.
    public static Capability Rehydrate(
        string capabilityId,
        string name,
        CapabilityType capabilityType,
        CapabilityStatus status,
        IReadOnlyList<CapabilityConfigurationField> configurationSchema,
        int configurationSchemaVersion,
        IReadOnlyDictionary<string, string> defaultConfiguration)
    {
        return new Capability
        {
            CapabilityId = capabilityId,
            Name = name,
            CapabilityType = capabilityType,
            Status = status,
            ConfigurationSchema = configurationSchema,
            ConfigurationSchemaVersion = configurationSchemaVersion,
            DefaultConfiguration = defaultConfiguration
        };
    }

    public void Update(
        string name,
        CapabilityType capabilityType,
        CapabilityStatus status,
        IReadOnlyList<CapabilityConfigurationField>? configurationSchema,
        int configurationSchemaVersion,
        IReadOnlyDictionary<string, string>? defaultConfiguration)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name;
        CapabilityType = capabilityType;
        Status = status;
        ConfigurationSchema = configurationSchema ?? EmptySchema;
        ConfigurationSchemaVersion = configurationSchemaVersion;
        DefaultConfiguration = defaultConfiguration ?? EmptyDefaults;
    }
}
