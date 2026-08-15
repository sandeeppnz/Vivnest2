namespace Vivnest.Cloud.Admin;

// Shared between both runtime-configuration publishers (decision-log.md
// ADR-064) - never let a credential-shaped key from Settings/
// DeviceCapability.Settings reach a real runtime blob. Device.Settings/
// DeviceCapability.Settings are unguarded plain-text dictionaries by
// deliberate design (ADR-050) - the Admin API already accepts/returns
// credentials in them as an accepted risk - but the write path must not
// also push them into a live blob, which would undermine ADR-038's
// local-only *.secrets.json boundary for that exact file. Matched
// case-insensitively against substrings, not an exact-name allowlist,
// since DeviceSettings/HomeAssistant already show more than one real
// credential field name (Password, RtspPassword, AccessToken) and a
// future capability could introduce another.
internal static class CredentialSettingsFilter
{
    private static readonly string[] CredentialFragments = ["password", "accesstoken", "secret"];

    public static IReadOnlyDictionary<string, string> Strip(
        IReadOnlyDictionary<string, string> settings,
        string context,
        List<string> warnings)
    {
        if (settings.Count == 0)
            return settings;

        var filtered = new Dictionary<string, string>();

        foreach (var (key, value) in settings)
        {
            if (CredentialFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(
                    $"\"{key}\" in {context} was present but not published - add it directly to the runtime's local .secrets.json file instead, per ADR-038.");
                continue;
            }

            filtered[key] = value;
        }

        return filtered;
    }
}
