using System;
using System.Collections.Generic;
using System.Text;

namespace Vivnest.Core.Options;


public class AgentOptions
{
    public string AgentId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int CaptureIntervalMinutes { get; set; } = 60;
}
