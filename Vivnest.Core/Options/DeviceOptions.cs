using System;
using System.Collections.Generic;
using System.Text;
using Vivnest.Core.Enums;

namespace Vivnest.Core.Options;
public class DeviceOptions
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceType Type { get; set; }
    public bool Enabled { get; set; }
    public DeviceSettings Settings { get; set; } = new();
}
