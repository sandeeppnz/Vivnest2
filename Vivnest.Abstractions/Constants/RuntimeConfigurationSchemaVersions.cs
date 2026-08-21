namespace Vivnest.Abstractions.Constants;

// Single source of truth for both publishers (Vivnest.Cloud) and the
// Runtime Adapter (Vivnest.Agent) - decision-log.md ADR-066. Bumping
// either version happens here, once, rather than as a magic number
// duplicated on both sides of the wire.
public static class RuntimeConfigurationSchemaVersions
{
    public const int CurrentDeviceSchemaVersion = 1;
    public const int CurrentAgentSchemaVersion = 1;
}
