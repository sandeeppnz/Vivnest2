using Vivnest.Core.SmartPlug.Models;

namespace Vivnest.Agent.Capabilities.SmartPlug;

public sealed record SmartPlugReadingCompletedEvent(
    SmartPlugReadingResult Result);
