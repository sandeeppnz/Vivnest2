using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vivnest.Core.Options.Heartbeats;

public class AgentHeartbeatOptions
{
    public bool Enabled { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }
}
