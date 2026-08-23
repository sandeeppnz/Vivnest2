using Vivnest.Core.SmartPlug.Models;

namespace Vivnest.Capabilities.SmartPlug;

public sealed record SmartPlugReadingFailedEvent(
    SmartPlugReadingFailureData Failure);
