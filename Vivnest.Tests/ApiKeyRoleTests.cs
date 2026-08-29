using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Entities;

namespace Vivnest.Tests;

// Roles-on-keys (2026-08-29): the User/Developer boundary the dashboard
// enforced client-side (PIN child-proofing) became a server gate keyed
// on ApiKeyEntity.Role. These pin the two rules everything hangs on:
// legacy keys grandfather to developer, and DevicesOnly always wins.
public class ApiKeyRoleTests
{
    private static TenantContext Context(bool devicesOnly, string? role) =>
        new("tenant-1", "site-1", devicesOnly, AgentId: null, Role: role, KeyName: "key");

    [Fact]
    public void LegacyKeyWithNoRoleIsDeveloper()
    {
        // A key created before Role existed deserializes with Role null.
        // It must keep the full access it has always had - the same
        // absent-defaults-to-permissive grandfathering DevicesOnly used
        // when it was introduced, and the reason the currently-deployed
        // dashboard key does not lose admin access on deploy.
        Assert.True(Context(devicesOnly: false, role: null).IsDeveloper);
    }

    [Fact]
    public void ExplicitRolesResolveAsWritten()
    {
        Assert.True(Context(devicesOnly: false, role: ApiKeyRoles.Developer).IsDeveloper);
        Assert.False(Context(devicesOnly: false, role: ApiKeyRoles.User).IsDeveloper);
    }

    [Fact]
    public void UnknownFutureRoleDegradesToNoDeveloperAccess()
    {
        // Fail closed: an unrecognized role value (a future addition, a
        // typo written straight into storage) must not grant the admin
        // surface.
        Assert.False(Context(devicesOnly: false, role: "operator").IsDeveloper);
    }

    [Fact]
    public void DevicesOnlyBeatsAnyRole()
    {
        // Scope is narrower than role: a devicesOnly key is never a
        // developer no matter what Role says.
        Assert.False(Context(devicesOnly: true, role: ApiKeyRoles.Developer).IsDeveloper);
        Assert.False(Context(devicesOnly: true, role: null).IsDeveloper);
    }

    [Fact]
    public void RoleValidationAcceptsExactlyTheTwoRoles()
    {
        Assert.True(ApiKeyRoles.IsValid(ApiKeyRoles.Developer));
        Assert.True(ApiKeyRoles.IsValid(ApiKeyRoles.User));
        Assert.False(ApiKeyRoles.IsValid(null));
        Assert.False(ApiKeyRoles.IsValid(""));
        Assert.False(ApiKeyRoles.IsValid("Developer"));
        Assert.False(ApiKeyRoles.IsValid("admin"));
    }

    [Fact]
    public void EntityRoleDefaultsToNullForLegacyRows()
    {
        // The CLR default IS the grandfathering mechanism - if Role ever
        // gains a non-null initializer, legacy rows would silently change
        // meaning on their next read.
        Assert.Null(new ApiKeyEntity { TenantId = "tenant-1", SiteId = "site-1" }.Role);
    }
}
