namespace Vivnest.Cloud.Auth;

// The two roles a full-access key can carry. Not an enum: the value
// round-trips through Table Storage and JSON as a plain string, and an
// unrecognized future value should degrade (to no developer access),
// not throw.
public static class ApiKeyRoles
{
    public const string Developer = "developer";
    public const string User = "user";

    public static bool IsValid(string? role) =>
        role is Developer or User;
}

// AgentId is null for an ordinary tenant/dashboard key and set for an
// agent key (see ApiKeyEntity.AgentId). Routes that only an Agent should
// be able to call compare it against the agent named in the route.
//
// Role/KeyName came with roles-on-keys (2026-08-29): Role gates the
// admin/action surface server-side (the dashboard's User/Developer split
// used to be client-side child-proofing only), KeyName feeds command
// audit (requestedBy) so history says who, not just "Dashboard".
public sealed record TenantContext(
    string TenantId,
    string SiteId,
    bool DevicesOnly,
    string? AgentId = null,
    string? Role = null,
    string? KeyName = null)
{
    // Null Role = a key created before roles existed - it keeps the full
    // access it has always had, the same grandfathering DevicesOnly used
    // when IT was introduced (absent-field-defaults-to-permissive).
    // DevicesOnly still wins: scope is narrower than any role.
    public bool IsDeveloper => !DevicesOnly && Role is null or ApiKeyRoles.Developer;
}
