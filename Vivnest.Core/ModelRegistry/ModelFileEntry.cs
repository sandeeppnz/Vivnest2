using System.Text.Json;

namespace Vivnest.Core.ModelRegistry;

// One file of a model version's file set (ADR-124). Lives in Core
// because it is the WIRE shape both sides parse: Cloud serializes the
// manifest into the projected agent settings' "ModelFiles" key (as a
// JSON string - the per-device model settings are a flat
// Dictionary<string, string>, the existing agent contract), and the
// High-type agent's ModelProvisioner parses it back to know what to
// download and which SHA-256 each file must hash to.
public sealed record ModelFileEntry(
    string Name,
    long SizeBytes,
    string Sha256,
    bool IsPrimary)
{
    public static string Serialize(IReadOnlyList<ModelFileEntry> files) =>
        JsonSerializer.Serialize(files);

    // Null on malformed input - callers treat that as "no usable
    // manifest" (a projection warning cloud-side, a fetch failure
    // agent-side), never an exception.
    public static IReadOnlyList<ModelFileEntry>? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<ModelFileEntry>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
