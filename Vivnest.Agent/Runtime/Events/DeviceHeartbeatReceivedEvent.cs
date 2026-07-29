using System;
using System.Collections.Generic;
using System.Text;
using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Agent.Runtime.Events;

public sealed record DeviceHeartbeatReceivedEvent(
    DeviceHeartbeat Heartbeat);
