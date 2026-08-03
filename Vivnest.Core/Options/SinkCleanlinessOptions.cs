namespace Vivnest.Core.Options;

// A camera-specific analysis, not a device type of its own - opt-in per
// camera via DeviceOptions.SinkCleanliness. Region coordinates are in the
// original capture's pixel space (not display-scaled), fixed because the
// camera doesn't move. See decision-log.md for the edge-density metric this
// threshold is tuned against.
public sealed class SinkCleanlinessOptions
{
    public bool Enabled { get; init; }

    public int RegionX { get; init; }
    public int RegionY { get; init; }
    public int RegionWidth { get; init; }
    public int RegionHeight { get; init; }

    /// <summary>
    /// Mean absolute grayscale-gradient score above which the region counts
    /// as NotClean. Validated against real samples (clean ~8-12, a
    /// genuinely stained/residue-filled sink ~16) rather than guessed - see
    /// decision-log.md. Tune per-camera if lighting/counter material differs.
    /// </summary>
    public double NotCleanThreshold { get; init; } = 14.0;
}
