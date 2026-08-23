namespace Vivnest.Cloud.Options;

// decision-log.md ADR-085 - the symmetric key both runtime-configuration
// publishers (Cloud) and Vivnest.Agent (each Agent process) need to hold
// independently for CredentialCipher to encrypt/decrypt sensitive Settings
// fields. Provisioned out of band on each side - Cloud via its own App
// Settings, Agent via the local-only common-config.secrets.json (ADR-038) -
// never itself carried inside a published config blob.
public sealed class CredentialEncryptionOptions
{
    public string? Key { get; set; }
}
