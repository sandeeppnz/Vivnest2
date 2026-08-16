using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Vivnest.Core.Security;

// decision-log.md ADR-085 - replaces the old strip-and-warn
// CredentialSettingsFilter (ADR-064, removed ADR-084) with encrypt-in-place:
// a sensitive Settings value is still published, but as AES-256-GCM
// ciphertext under a shared symmetric key (CredentialEncryptionOptions),
// never as plaintext. The key never travels through the blob it protects -
// Cloud and each Agent hold it independently (see CredentialEncryptionOptions).
//
// Field classification (IsCredentialField) only matters at encrypt time, on
// the Cloud side. Decryption doesn't need it at all: DecryptInPlace just
// looks for the "enc:v1:" prefix, which only Encrypt ever produces, so any
// string carrying it IS a credential value by construction - this lets the
// Agent-side decrypt walk be a blind, generic JSON-tree walk instead of
// needing to know which keys are Settings dictionaries.
public static class CredentialCipher
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private static readonly string[] CredentialFragments = ["password", "accesstoken", "secret", "connectionstring"];

    public static bool IsCredentialField(string key) =>
        CredentialFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public static byte[] ParseKey(string base64Key)
    {
        var key = Convert.FromBase64String(base64Key);

        if (key.Length != KeySize)
        {
            throw new ArgumentException(
                $"CredentialEncryption key must decode to {KeySize} bytes (AES-256); got {key.Length}.",
                nameof(base64Key));
        }

        return key;
    }

    public static string Encrypt(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    // Cloud-side helper - encrypts every credential-shaped key in a
    // Settings dictionary, leaves the rest untouched. Both publishers call
    // this identically for Device.Settings, each capability's own Settings,
    // and each Agent device entry's ObjectDetection/SinkCleanliness settings.
    public static IReadOnlyDictionary<string, string> EncryptFields(
        IReadOnlyDictionary<string, string> settings, byte[] key)
    {
        if (settings.Count == 0)
            return settings;

        var result = new Dictionary<string, string>();

        foreach (var (fieldKey, value) in settings)
            result[fieldKey] = IsCredentialField(fieldKey) ? Encrypt(value, key) : value;

        return result;
    }

    public static bool TryDecrypt(string value, byte[] key, out string plaintext)
    {
        plaintext = value;

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        try
        {
            var payload = Convert.FromBase64String(value[Prefix.Length..]);

            if (payload.Length < NonceSize + TagSize)
                return false;

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var ciphertext = payload.AsSpan(NonceSize + TagSize);
            var plaintextBytes = new byte[ciphertext.Length];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            }

            plaintext = Encoding.UTF8.GetString(plaintextBytes);
            return true;
        }
        catch (Exception)
        {
            // Malformed base64, wrong key, tampered ciphertext (GCM's tag
            // check fails), truncated payload - all just mean "not
            // decryptable," reported the same way as "not encrypted at
            // all" to the caller. Never throws into Agent startup.
            return false;
        }
    }

    // Agent-side helper - recursively decrypts every string leaf under node
    // that carries the "enc:v1:" prefix, in place. Deliberately doesn't
    // walk by key name (unlike EncryptFields) - see the type-level comment.
    public static void DecryptInPlace(JsonNode? node, byte[] key)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var propertyKey in obj.Select(p => p.Key).ToList())
                {
                    if (obj[propertyKey] is JsonValue value
                        && value.TryGetValue<string>(out var raw)
                        && TryDecrypt(raw, key, out var decrypted))
                    {
                        obj[propertyKey] = decrypted;
                    }
                    else
                    {
                        DecryptInPlace(obj[propertyKey], key);
                    }
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    DecryptInPlace(item, key);
                break;
        }
    }
}
