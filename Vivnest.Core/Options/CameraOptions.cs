using System;
using System.Collections.Generic;
using System.Text;

namespace Vivnest.Core.Options;

public class CameraOptions
{
    public string CameraId { get; set; } = "";
    public string Host { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public string RtspUsername { get; set; } = "";

    public string RtspPassword { get; set; } = "";
    public TimeSpan CaptureInterval { get; init; }


}
