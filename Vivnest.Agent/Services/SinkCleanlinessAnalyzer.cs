using SkiaSharp;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Services;

// Metric validated against real capture samples before this was written
// (throwaway spike, not committed) - mean absolute grayscale-gradient
// magnitude in a fixed ROI. Chosen over raw pixel-diff-against-a-reference
// because it's far more tolerant of day/night lighting swings: a clean,
// smooth stainless basin has low local contrast regardless of brightness,
// while residue/stains add texture that survives the lighting change. See
// decision-log.md for the validation numbers and the ROI's derivation.
public sealed class SinkCleanlinessAnalyzer : ISinkCleanlinessAnalyzer
{
    public SinkCleanlinessResult Analyze(byte[] imageBytes, SinkCleanlinessOptions options)
    {
        using var bitmap = SKBitmap.Decode(imageBytes);

        var rect = new SKRectI(
            options.RegionX,
            options.RegionY,
            options.RegionX + options.RegionWidth,
            options.RegionY + options.RegionHeight);

        using var region = new SKBitmap(options.RegionWidth, options.RegionHeight);

        bitmap.ExtractSubset(region, rect);

        var score = EdgeDensity(region);

        return new SinkCleanlinessResult
        {
            Clean = score < options.NotCleanThreshold,
            Score = score,
        };
    }

    private static double EdgeDensity(SKBitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var gray = new byte[w, h];

        for (var py = 0; py < h; py++)
        {
            for (var px = 0; px < w; px++)
            {
                var c = bitmap.GetPixel(px, py);
                gray[px, py] = (byte)((c.Red + c.Green + c.Blue) / 3);
            }
        }

        long total = 0;
        var count = 0;

        for (var py = 0; py < h; py++)
        {
            for (var px = 0; px < w; px++)
            {
                if (px + 1 < w)
                {
                    total += Math.Abs(gray[px, py] - gray[px + 1, py]);
                    count++;
                }

                if (py + 1 < h)
                {
                    total += Math.Abs(gray[px, py] - gray[px, py + 1]);
                    count++;
                }
            }
        }

        return (double)total / count;
    }
}
