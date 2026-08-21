namespace Vivnest.Abstractions.Configuration;

// Thrown by DeviceConfigRuntimeAdapter.Adapt when a device-config
// document declares a SchemaVersion this Agent build doesn't recognize
// (decision-log.md ADR-066) - the Agent must never silently try to bind
// a shape it doesn't understand. Callers must catch this per-device (see
// Program.cs's TryLoadRemoteDeviceConfigsAsync) so one unsupported
// device never takes every other device down with it.
public sealed class UnsupportedConfigurationSchemaException : Exception
{
    public UnsupportedConfigurationSchemaException(string message) : base(message)
    {
    }
}
