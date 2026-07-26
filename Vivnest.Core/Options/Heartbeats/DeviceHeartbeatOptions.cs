namespace Vivnest.Core.Options.Heartbeats;

public sealed class DeviceHeartbeatOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string TableName { get; set; } = "tblDeviceHeartbeat";
}