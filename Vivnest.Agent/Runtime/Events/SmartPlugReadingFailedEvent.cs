using Vivnest.Core.SmartPlug.Models;

namespace Vivnest.Agent.Runtime.Events;

public sealed record SmartPlugReadingFailedEvent(
    SmartPlugReadingFailureData Failure);
