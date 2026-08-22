using System.Globalization;
using System.Text.Json;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

public sealed class CapabilityConfigurationService : ICapabilityConfigurationService
{
    // ADR-100 - projection-time defaulting. Reads schema and defaults
    // straight off the stored entity so that the projector does not need a
    // fourth private copy of this JSON parsing (CapabilityAssignmentService
    // and CapabilityManagementService each keep their own entity->domain
    // mapper by the codebase's "each service maps its own way" convention;
    // a third for a caller that only needs two fields would be worse than
    // putting the parsing where the schema already lives).
    //
    // Malformed schema or defaults degrade to "no defaults" rather than
    // throwing: a publish must not fail because reference data is broken in
    // a way this device's own assignment did nothing to cause.
    public IReadOnlyDictionary<string, string> ResolveEffectiveSettings(
        CapabilityEntity capability,
        IReadOnlyDictionary<string, string>? storedSettings)
    {
        ArgumentNullException.ThrowIfNull(capability);

        var merged = new Dictionary<string, string>(
            storedSettings ?? new Dictionary<string, string>());

        List<CapabilityConfigurationFieldDto>? schema = null;
        Dictionary<string, string>? defaults = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(capability.ConfigurationSchema))
            {
                schema = JsonSerializer.Deserialize<List<CapabilityConfigurationFieldDto>>(
                    capability.ConfigurationSchema);
            }

            if (!string.IsNullOrWhiteSpace(capability.DefaultConfiguration))
            {
                defaults = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    capability.DefaultConfiguration);
            }
        }
        catch (JsonException)
        {
            return merged;
        }

        if (schema == null)
            return merged;

        foreach (var field in schema)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || merged.ContainsKey(field.Name))
                continue;

            if (defaults != null && defaults.TryGetValue(field.Name, out var capabilityDefault))
                merged[field.Name] = capabilityDefault;
            else if (field.DefaultValue != null)
                merged[field.Name] = field.DefaultValue;
        }

        return merged;
    }

    public IReadOnlyDictionary<string, string> ApplyDefaults(
        Capability capability,
        IReadOnlyDictionary<string, string>? suppliedSettings)
    {
        var merged = new Dictionary<string, string>(suppliedSettings ?? new Dictionary<string, string>());

        foreach (var field in capability.ConfigurationSchema)
        {
            if (merged.ContainsKey(field.Name))
                continue;

            if (capability.DefaultConfiguration.TryGetValue(field.Name, out var capabilityDefault))
            {
                merged[field.Name] = capabilityDefault;
            }
            else if (field.DefaultValue != null)
            {
                merged[field.Name] = field.DefaultValue;
            }
        }

        return merged;
    }

    public bool Validate(
        Capability capability,
        IReadOnlyDictionary<string, string> settings,
        out IReadOnlyList<string> errors)
    {
        var found = new List<string>();

        foreach (var field in capability.ConfigurationSchema)
        {
            var hasValue = settings.TryGetValue(field.Name, out var rawValue);

            if (!hasValue || string.IsNullOrWhiteSpace(rawValue))
            {
                if (field.Required)
                    found.Add($"'{field.Name}' is required.");

                continue;
            }

            switch (field.Type)
            {
                case CapabilityConfigurationFieldType.Number:
                    if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        found.Add($"'{field.Name}' must be a number.");
                        break;
                    }

                    if (field.Minimum.HasValue && number < field.Minimum.Value)
                        found.Add($"'{field.Name}' must be >= {field.Minimum.Value}.");

                    if (field.Maximum.HasValue && number > field.Maximum.Value)
                        found.Add($"'{field.Name}' must be <= {field.Maximum.Value}.");

                    break;

                case CapabilityConfigurationFieldType.Boolean:
                    if (!bool.TryParse(rawValue, out _))
                        found.Add($"'{field.Name}' must be true or false.");

                    break;

                case CapabilityConfigurationFieldType.String:
                    if (field.AllowedValues is { Count: > 0 } allowed && !allowed.Contains(rawValue))
                        found.Add($"'{field.Name}' must be one of: {string.Join(", ", allowed)}.");

                    break;
            }
        }

        errors = found;
        return found.Count == 0;
    }
}
