using Vivnest.Domain.Agents;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Tenants;

namespace Vivnest.Domain.Capabilities;

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

    // The stable string the Agent's capability manifests are named by
    // ("camera.capture"). CapabilityId stays the registry's GUID identity;
    // this is the vocabulary that joins a Cloud assignment to a running
    // implementation - see decision-log.md ADR-096. Optional on the
    // entity so pre-2026-08-22 rows still rehydrate, but a capability
    // without one can never be matched by an Agent, which is why the
    // projector refuses to publish it.
    public string? Key { get; private set; }

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
        IReadOnlyDictionary<string, string>? defaultConfiguration = null,
        string? key = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        CapabilityId = Guid.NewGuid().ToString();
        Name = name;
        Key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
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
        string? key,
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
            Key = string.IsNullOrWhiteSpace(key) ? null : key.Trim(),
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
        IReadOnlyDictionary<string, string>? defaultConfiguration,
        string? key = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        // A null key leaves the existing one alone, so an Update from a
        // caller that predates this field cannot silently erase it.
        if (!string.IsNullOrWhiteSpace(key))
            Key = key.Trim();

        Name = name;
        CapabilityType = capabilityType;
        Status = status;
        ConfigurationSchema = configurationSchema ?? EmptySchema;
        ConfigurationSchemaVersion = configurationSchemaVersion;
        DefaultConfiguration = defaultConfiguration ?? EmptyDefaults;
    }
}
