namespace Vivnest.Core.Options;

public sealed class DeviceSettings
{
    public string Host { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string RtspUsername { get; set; } = "";
    public string RtspPassword { get; set; } = "";

    // Tapo hub child devices (e.g. a T100 motion sensor under an H100 hub)
    // are addressed by this id via the hub's control_child wrapper - Host/
    // Username/Password above are the hub's, not the child's, since the
    // child has no network presence of its own.
    public string ChildDeviceId { get; set; } = "";
}