namespace Vivnest.Core.Options;

public class DeviceEventOptions
{
    // On by default (ADR-120): device events are the product's activity
    // feed; "config section missing" silently meaning "no events" was a
    // trap, proven by the 2026-08-29 rebuild.
    public bool Enabled { get; set; } = true;
}