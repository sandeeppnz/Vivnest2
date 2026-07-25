namespace Vivnest.Core.Options;

public sealed class DeviceSettings
{
    public string Host { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string RtspUsername { get; set; } = "";
    public string RtspPassword { get; set; } = "";
    public TimeSpan CaptureInterval { get; init; }

}

//public sealed class CameraDeviceOptions : DeviceOptions
//{
//    public string Host { get; set; } = string.Empty;
//    public string Username { get; set; } = string.Empty;
//    public string Password { get; set; } = string.Empty;
//    public string RtspUsername { get; set; } = string.Empty;
//    public string RtspPassword { get; set; } = string.Empty;
//    public TimeSpan CaptureInterval { get; set; }
//}