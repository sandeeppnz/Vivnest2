namespace Vivnest.Cloud.Api.Dtos;

// TenantName/SiteName are looked up from tblTenants/tblSites at call
// time (ADR-055's follow-up - the dashboard topbar used to show TenantId/
// SiteId directly, which was readable back when those were caller-chosen
// strings like "Sana"/"1Fitz", but is a raw Guid now). Nullable - a key
// can be scoped to a Tenant/Site that's since been deleted/never existed;
// the dashboard falls back to the id in that case rather than erroring.
//
// Role is the EFFECTIVE role ("developer" | "user") - legacy keys with
// nothing stored report developer, matching what the gates enforce.
// KeyName is the key's admin-given name, which the dashboard shows and
// the command audit (requestedBy) records.
public sealed record WhoAmIResponse(
    string TenantId,
    string SiteId,
    bool DevicesOnly,
    string Role,
    string? KeyName,
    string? TenantName,
    string? SiteName);
