using Vivnest.Core.Enums;

namespace Vivnest.Core.Domain;

// One field in a Capability's ConfigurationSchema (decision-log.md
// ADR-062, Phase 5) - e.g. ObjectDetection's "confidenceThreshold"
// (Number, required, 0..1) or "model" (String, required). Deliberately a
// plain value object, not its own entity/table (Vivnest.Core.Domain has
// no CapabilityConfigurationFieldEntity) - it has no independent
// lifecycle, it's structure that belongs to Capability, serialized as
// part of CapabilityEntity.ConfigurationSchema (JSON array), same
// reasoning the spec itself gives for not creating a table "just because
// a relationship exists."
public sealed class CapabilityConfigurationField
{
    public string Name { get; private set; } = null!;

    public CapabilityConfigurationFieldType Type { get; private set; }

    public bool Required { get; private set; }

    // Number-only. Ignored by CapabilityConfigurationService for other types.
    public double? Minimum { get; private set; }

    public double? Maximum { get; private set; }

    // String-only - e.g. imageQuality: low/medium/high. Ignored for other types.
    public IReadOnlyList<string>? AllowedValues { get; private set; }

    public string? DefaultValue { get; private set; }

    public CapabilityConfigurationField(
        string name,
        CapabilityConfigurationFieldType type,
        bool required = false,
        double? minimum = null,
        double? maximum = null,
        IReadOnlyList<string>? allowedValues = null,
        string? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        if (minimum.HasValue && maximum.HasValue && minimum.Value > maximum.Value)
            throw new ArgumentException("Minimum cannot be greater than Maximum.", nameof(minimum));

        Name = name;
        Type = type;
        Required = required;
        Minimum = minimum;
        Maximum = maximum;
        AllowedValues = allowedValues;
        DefaultValue = defaultValue;
    }
}
