using System.Globalization;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

public sealed class CapabilityConfigurationService : ICapabilityConfigurationService
{
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
