namespace Vivnest.Core.Options;

public class AgentEventOptions
{
    // On by default (ADR-120), same grounds as DeviceEventOptions.
    public bool Enabled { get; set; } = true;
}
