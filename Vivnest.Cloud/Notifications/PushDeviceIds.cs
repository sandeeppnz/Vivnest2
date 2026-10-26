using System.Security.Cryptography;
using System.Text;

namespace Vivnest.Cloud.Notifications;

public static class PushDeviceIds
{
    // 16 hex chars of SHA-256: stable, opaque (the token itself is not
    // safe to put in a URL or a log line), and collision-proof at the
    // handful of phones one site will ever have.
    public static string ForToken(string expoPushToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(expoPushToken.Trim()));

        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public static bool LooksLikeExpoToken(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.Length < 200
        && (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal)
            || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
        && token.EndsWith(']');
}
