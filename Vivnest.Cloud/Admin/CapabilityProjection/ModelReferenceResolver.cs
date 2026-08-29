using System.Globalization;
using Vivnest.Cloud.Entities;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.ModelRegistry;

namespace Vivnest.Cloud.Admin.CapabilityProjection;

public sealed record ModelReferenceResolution(
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string> Warnings);

public interface IModelReferenceResolver
{
    Task<ModelReferenceResolution> ResolveAsync(
        string tenantId,
        string siteId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}

// Publish-time model resolution (ADR-124): when an assignment's settings
// carry a "ModelId", resolve it to a concrete version - the "ModelVersion"
// pin if present, else the latest Active version - and write the resolved
// "ModelVersion" plus the "ModelFiles" manifest (JSON, ModelFileEntry[])
// into the settings the projector sees. Runs BEFORE ICapabilityRuntimeProjector
// (which is deliberately synchronous) in both configuration projectors, so
// the published, versioned config snapshots the exact model version:
// activating a new version changes nothing until a publish, and rolling a
// config version back rolls the model reference back with it.
//
// Settings without a ModelId pass through untouched - non-model
// capabilities and legacy ModelPath assignments never pay for this.
// Dangling references (unknown model, no Active version, missing pin)
// come back as warnings; the ROI projector's own "no model" gate then
// blocks the publish with both messages visible.
public sealed class ModelReferenceResolver : IModelReferenceResolver
{
    public const string ModelIdKey = "ModelId";
    public const string ModelVersionKey = "ModelVersion";
    public const string ModelFilesKey = "ModelFiles";

    private readonly IModelStore _models;
    private readonly IModelVersionStore _versions;

    public ModelReferenceResolver(IModelStore models, IModelVersionStore versions)
    {
        _models = models;
        _versions = versions;
    }

    public async Task<ModelReferenceResolution> ResolveAsync(
        string tenantId,
        string siteId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.TryGetValue(ModelIdKey, out var modelId) || string.IsNullOrWhiteSpace(modelId))
            return new ModelReferenceResolution(settings, []);

        var model = await _models.GetAsync(tenantId, siteId, modelId.Trim(), cancellationToken);

        if (model == null)
        {
            return Warn(settings,
                $"ModelId \"{modelId}\" doesn't exist in the model registry.");
        }

        if (string.Equals(model.Status, "Retired", StringComparison.OrdinalIgnoreCase))
        {
            return Warn(settings,
                $"Model \"{model.Name}\" ({modelId}) is Retired.");
        }

        ModelVersionEntity? resolved;

        if (settings.TryGetValue(ModelVersionKey, out var pin) && !string.IsNullOrWhiteSpace(pin))
        {
            // An explicit pin may point at a Retired version deliberately
            // (retiring only removes a version from DEFAULT resolution) -
            // no warning for that, only for a pin that doesn't exist.
            if (!int.TryParse(pin, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pinned))
            {
                return Warn(settings,
                    $"ModelVersion \"{pin}\" is not a whole number.");
            }

            resolved = await _versions.GetAsync(
                tenantId, siteId, model.RowKey, pinned, cancellationToken);

            if (resolved == null)
            {
                return Warn(settings,
                    $"Model \"{model.Name}\" has no version {pinned}.");
            }
        }
        else
        {
            var all = await _versions.ListAsync(
                tenantId, siteId, model.RowKey, cancellationToken);

            resolved = all
                .Where(v => string.IsNullOrWhiteSpace(v.Status) ||
                            string.Equals(v.Status, "Active", StringComparison.OrdinalIgnoreCase))
                .MaxBy(v => v.Version);

            if (resolved == null)
            {
                return Warn(settings,
                    $"Model \"{model.Name}\" has no Active version to resolve - upload one or pin a version.");
            }
        }

        if (ModelFileEntry.TryDeserialize(resolved.Files) is not { Count: > 0 })
        {
            return Warn(settings,
                $"Model \"{model.Name}\" v{resolved.Version} has a corrupt file manifest.");
        }

        var augmented = new Dictionary<string, string>(settings)
        {
            [ModelIdKey] = model.RowKey,
            [ModelVersionKey] = resolved.Version.ToString(CultureInfo.InvariantCulture),
            [ModelFilesKey] = resolved.Files,
        };

        return new ModelReferenceResolution(augmented, []);
    }

    private static ModelReferenceResolution Warn(
        IReadOnlyDictionary<string, string> settings, string warning) =>
        new(settings, [warning]);
}
