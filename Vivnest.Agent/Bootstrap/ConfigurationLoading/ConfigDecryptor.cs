using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Vivnest.Core.Security;

namespace Vivnest.Agent.Bootstrap.ConfigurationLoading;

// Holds the credential-encryption key (or the fact that there isn't one)
// for the duration of a configuration load, and decrypts enc:v1: values
// in the documents that pass through. The key must come from the
// bootstrap configuration that already exists before remote
// configuration is loaded - it cannot itself arrive encrypted.
internal sealed class ConfigDecryptor
{
    private readonly byte[]? _key;
    private readonly StartupErrorSink _errors;

    private ConfigDecryptor(
        byte[]? key,
        StartupErrorSink errors)
    {
        _key = key;
        _errors = errors;
    }

    public static ConfigDecryptor FromConfiguration(
        IConfiguration configuration,
        StartupErrorSink errors)
    {
        var rawKey =
            configuration["CredentialEncryption:Key"];

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            Console.WriteLine(
                "[Startup] CredentialEncryption:Key is not set; " +
                "encrypted config fields will not be decrypted.");

            return new ConfigDecryptor(null, errors);
        }

        try
        {
            return new ConfigDecryptor(
                CredentialCipher.ParseKey(rawKey),
                errors);
        }
        catch (Exception ex)
        {
            errors.Report(
                "[Startup] CredentialEncryption:Key is invalid, " +
                "encrypted config fields will not be decrypted: " +
                $"{ex.Message}");

            return new ConfigDecryptor(null, errors);
        }
    }

    // Decrypts a whole JSON document's enc:v1: values, returning the
    // re-serialized bytes. Never throws: without a key, or when
    // decryption fails, the document is returned as-is - with a loud
    // warning if it visibly contains encrypted values, because using
    // those undecrypted produces failures that look unrelated (an RTSP
    // password stays the literal "enc:v1:..." string and the camera
    // refuses it - nowhere near the missing key that caused it).
    public byte[] Decrypt(byte[] configBytes)
    {
        if (_key == null)
        {
            // The sibling path below - decryption attempted and failed -
            // already warned about this. The no-key path did not, even
            // though it is the likelier misconfiguration: an unset
            // environment variable rather than a wrong one.
            if (ContainsEncryptedValues(StripUtf8Bom(configBytes)))
            {
                _errors.Report(
                    "[Startup] WARNING: config contains enc:v1: values but " +
                    "CredentialEncryption:Key is not set, so they are being " +
                    "used UNDECRYPTED. Expect failures that look unrelated.");
            }

            return configBytes;
        }

        var json =
            StripUtf8Bom(configBytes);

        try
        {
            var node =
                JsonNode.Parse(json);

            CredentialCipher.DecryptInPlace(
                node,
                _key);

            return JsonSerializer.SerializeToUtf8Bytes(
                node);
        }
        catch (Exception ex)
        {
            _errors.Report(
                "[Startup] Failed to decrypt downloaded config, " +
                $"using it as-is: {ex.Message}");

            if (ContainsEncryptedValues(json))
            {
                _errors.Report(
                    "[Startup] WARNING: that config contains " +
                    "enc:v1: values which are now being used " +
                    "UNDECRYPTED. Expect failures that look " +
                    "unrelated.");
            }

            return json;
        }
    }

    // Decrypts a parsed device document in place. Without a key it warns
    // the same way Decrypt does - this path used to be silent, so a fleet
    // whose key went missing saw every camera "refuse its credentials"
    // (the literal enc:v1: string) with nothing pointing at the key. The
    // documentName puts the device id in the error, so the heartbeat
    // shows which devices are affected.
    public void TryDecryptInPlace(
        JsonObject document,
        string documentName)
    {
        if (_key != null)
        {
            CredentialCipher.DecryptInPlace(
                document,
                _key);

            return;
        }

        if (document.ToJsonString().Contains(
                "enc:v1:",
                StringComparison.Ordinal))
        {
            _errors.Report(
                $"[Startup] WARNING: {documentName} contains enc:v1: values " +
                "but CredentialEncryption:Key is not set, so they are being " +
                "used UNDECRYPTED. Expect failures that look unrelated.");
        }
    }

    internal static byte[] StripUtf8Bom(
        byte[] bytes)
    {
        return bytes.Length >= 3 &&
               bytes[0] == 0xEF &&
               bytes[1] == 0xBB &&
               bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;
    }

    private static bool ContainsEncryptedValues(
        byte[] jsonBytes)
    {
        try
        {
            return Encoding.UTF8
                .GetString(jsonBytes)
                .Contains(
                    "enc:v1:",
                    StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
