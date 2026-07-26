using System;
using System.Collections.Generic;
using System.Text;

namespace Vivnest.Core.Options;


public class AgentOptions
{
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
}
