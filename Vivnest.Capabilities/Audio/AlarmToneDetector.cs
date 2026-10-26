using Vivnest.Core.Devices.Audio;

namespace Vivnest.Capabilities.Audio;

public sealed record AlarmDetection(
    bool Detected,
    int BurstCount,
    double? FrequencyHz,
    TimeSpan LongestTone)
{
    public static readonly AlarmDetection None = new(false, 0, null, TimeSpan.Zero);
}

public interface IAlarmToneDetector
{
    AlarmDetection Analyse(AudioClip clip);
}

// Looks for what a smoke/fire alarm sounds like rather than what it means:
// a loud, narrow-band tone around 3 kHz that either repeats in bursts (the
// T3 pattern is three beeps and a pause; T4 is four) or simply sustains.
// No model, no training data - a Goertzel filter bank over short frames,
// cheap enough for a Raspberry Pi to run on every clip.
//
// What it can and cannot tell you. It hears "an alarm-like tone", not "a
// fire". A microwave's end-of-cycle beeps, a reversing lorry or a doorbell
// in the same band can trigger it, which is why the burst frequencies must
// agree with each other and why the worker applies a cooldown. Anything
// subtler than that - telling a smoke alarm from a carbon-monoxide alarm,
// or either from a lookalike - needs a trained classifier, and that is the
// upgrade if this one proves too noisy in practice.
public sealed class AlarmToneDetector : IAlarmToneDetector
{
    // Smoke alarms are standardised to 3.1 kHz +/- a few hundred; the band
    // is widened a little either side for cheap units and for Doppler-free
    // mics that colour the response.
    private const double LowHz = 2500;
    private const double HighHz = 4000;

    // Goertzel bin spacing. A 50 ms frame resolves ~20 Hz, so 25 Hz steps
    // cannot step over a tone sitting between two bins.
    private const double StepHz = 25;

    private const int FrameMilliseconds = 50;

    // Fraction of the frame's (windowed) energy that must sit in the single
    // strongest bin, in the form peakPower / (N * energy). Under a Hann
    // window a pure tone scores about 1/6 = 0.167 and white noise about
    // 0.005, so 0.08 is roughly half a pure tone: it passes a tone through
    // a fair amount of room noise and rejects broadband sound outright.
    private const double TonalThreshold = 0.08;

    // Below this RMS (full scale = 1.0) a frame is treated as silence, so a
    // quiet room's hiss cannot register as a tone by being the loudest thing
    // in an empty frame.
    private const double MinRms = 0.01;

    private const int MinBurstFrames = 4;       // 200 ms: a real beep, not a click
    private const int MaxGapFramesFilled = 1;   // 50 ms dropout inside a beep
    private const int MinBursts = 3;            // T3
    private const int SustainedFrames = 60;     // 3 s unbroken: continuous siren
    private const double BurstFrequencySpreadHz = 150;

    public AlarmDetection Analyse(AudioClip clip)
    {
        var rate = clip.SampleRate;
        var frameLength = rate * FrameMilliseconds / 1000;

        if (frameLength < 16 || clip.Samples.Length < frameLength * 2)
            return AlarmDetection.None;

        var bins = BuildBins(rate, frameLength);
        var window = BuildHannWindow(frameLength);

        var frameCount = clip.Samples.Length / frameLength;
        var tonal = new bool[frameCount];
        var frequency = new double[frameCount];
        var windowed = new double[frameLength];

        for (var f = 0; f < frameCount; f++)
        {
            var offset = f * frameLength;

            double sumSquares = 0;
            double energy = 0;

            for (var i = 0; i < frameLength; i++)
            {
                var sample = clip.Samples[offset + i];
                sumSquares += sample * sample;

                windowed[i] = sample * window[i];
                energy += windowed[i] * windowed[i];
            }

            if (Math.Sqrt(sumSquares / frameLength) < MinRms || energy <= 0)
                continue;

            var bestPower = 0.0;
            var bestHz = 0.0;

            foreach (var (hz, coefficient) in bins)
            {
                var power = GoertzelPower(windowed, coefficient);

                if (power > bestPower)
                {
                    bestPower = power;
                    bestHz = hz;
                }
            }

            if (bestPower / (frameLength * energy) >= TonalThreshold)
            {
                tonal[f] = true;
                frequency[f] = bestHz;
            }
        }

        FillShortGaps(tonal);

        var runs = FindRuns(tonal, frequency);

        var longest = runs.Count == 0 ? 0 : runs.Max(r => r.Length);
        var longestTone = TimeSpan.FromMilliseconds(longest * FrameMilliseconds);

        var bursts = runs.Where(r => r.Length >= MinBurstFrames).ToList();

        if (bursts.Count == 0)
            return AlarmDetection.None with { LongestTone = longestTone };

        // A burst only counts toward the pattern if its pitch agrees with
        // the others'. Three different notes are a melody, not an alarm.
        var medianHz = Median(bursts.Select(b => b.MedianHz).ToList());

        var agreeing = bursts
            .Where(b => Math.Abs(b.MedianHz - medianHz) <= BurstFrequencySpreadHz)
            .ToList();

        var sustained = agreeing.Any(b => b.Length >= SustainedFrames);
        var repeating = agreeing.Count >= MinBursts;

        return new AlarmDetection(
            sustained || repeating,
            agreeing.Count,
            agreeing.Count > 0 ? medianHz : null,
            longestTone);
    }

    private readonly record struct Run(int Length, double MedianHz);

    // A 50 ms dropout in the middle of a beep (a hiccup in the stream, a
    // frame where the mic clipped) must not split one beep into two short
    // ones that each fail MinBurstFrames.
    private static void FillShortGaps(bool[] tonal)
    {
        var i = 0;

        while (i < tonal.Length)
        {
            if (tonal[i])
            {
                i++;
                continue;
            }

            var gapStart = i;

            while (i < tonal.Length && !tonal[i])
                i++;

            var gapLength = i - gapStart;

            if (gapLength <= MaxGapFramesFilled && gapStart > 0 && i < tonal.Length)
            {
                for (var g = gapStart; g < i; g++)
                    tonal[g] = true;
            }
        }
    }

    private static List<Run> FindRuns(bool[] tonal, double[] frequency)
    {
        var runs = new List<Run>();
        var i = 0;

        while (i < tonal.Length)
        {
            if (!tonal[i])
            {
                i++;
                continue;
            }

            var start = i;

            while (i < tonal.Length && tonal[i])
                i++;

            var hz = new List<double>();

            for (var f = start; f < i; f++)
            {
                if (frequency[f] > 0)
                    hz.Add(frequency[f]);
            }

            runs.Add(new Run(i - start, hz.Count == 0 ? 0 : Median(hz)));
        }

        return runs;
    }

    private static double Median(List<double> values)
    {
        values.Sort();

        var middle = values.Count / 2;

        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }

    private static List<(double Hz, double Coefficient)> BuildBins(int sampleRate, int frameLength)
    {
        var bins = new List<(double, double)>();

        for (var hz = LowHz; hz <= HighHz; hz += StepHz)
        {
            var k = hz * frameLength / sampleRate;
            bins.Add((hz, 2 * Math.Cos(2 * Math.PI * k / frameLength)));
        }

        return bins;
    }

    private static double[] BuildHannWindow(int length)
    {
        var window = new double[length];

        for (var i = 0; i < length; i++)
            window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (length - 1)));

        return window;
    }

    private static double GoertzelPower(double[] samples, double coefficient)
    {
        double s1 = 0;
        double s2 = 0;

        foreach (var sample in samples)
        {
            var s0 = sample + coefficient * s1 - s2;
            s2 = s1;
            s1 = s0;
        }

        return s1 * s1 + s2 * s2 - coefficient * s1 * s2;
    }
}
