namespace Vivnest.Cloud.Admin.Interfaces;

// One line per catalogue object touched, in "kind: name - action" form,
// so the caller (dashboard seed button, bootstrap flow) can show
// exactly what happened without a schema of its own.
public sealed record CatalogueSeedReport(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Repaired,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> Warnings);

public interface ICatalogueSeedService
{
    // Idempotent reconciliation of the capability catalogue, device
    // types, and their compatibility links against CatalogueSeed - the
    // canonical, in-code manifest (ADR-119). Creates what's missing,
    // repairs identity drift (name/key), and never touches an existing
    // row's admin-customized schema or defaults.
    Task<CatalogueSeedReport> SeedAsync(CancellationToken cancellationToken = default);
}
