namespace Vivnest.Abstractions.Models.Api;

// TenantName/SiteName are looked up from tblTenants/tblSites at call
// time (ADR-055's follow-up - the dashboard topbar used to show TenantId/
// SiteId directly, which was readable back when those were caller-chosen
// strings like "Sana"/"1Fitz", but is a raw Guid now). Nullable - a key
// can be scoped to a Tenant/Site that's since been deleted/never existed;
// the dashboard falls back to the id in that case rather than erroring.
public sealed record WhoAmIResponse(
    string TenantId,
    string SiteId,
    bool DevicesOnly,
    string? TenantName,
    string? SiteName);
