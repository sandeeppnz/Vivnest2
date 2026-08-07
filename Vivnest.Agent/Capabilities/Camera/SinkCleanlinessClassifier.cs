using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Camera;

// ONNX classifier over a fixed ROI - see ADR-032. Preprocessing here
// (input size, ImageNet mean/std normalization) must stay in lock-step
// with scripts/ml/sink-cleanliness/train.py's transforms; a mismatch
// wouldn't fail loudly, it would just make the model quietly wrong.
public sealed class SinkCleanlinessClassifier : ISinkCleanlinessClassifier, IDisposable
{
    private const int InputSize = 224;

    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    private readonly ILogger<SinkCleanlinessClassifier> _logger;

    // Keyed by ModelPath so multiple cameras could point at different
    // models without reloading one every capture - in practice today
    // there's one entry. A null value means loading already failed for
    // that path; cached too, so a missing/bad model logs once at startup
    // instead of once per capture.
    private readonly ConcurrentDictionary<string, InferenceSession?> _sessions = new();

    public SinkCleanlinessClassifier(ILogger<SinkCleanlinessClassifier> logger)
    {
        _logger = logger;
    }

    public SinkCleanlinessResult? Classify(byte[] imageBytes, SinkCleanlinessOptions options)
    {
        var session = _sessions.GetOrAdd(options.ModelPath, LoadSession);

        if (session is null)
            return null;

        using var source = SKBitmap.Decode(imageBytes);

        if (source is null)
        {
            _logger.LogWarning("Could not decode capture for sink-cleanliness classification.");
            return null;
        }

        var roiWidth = options.RoiRight - options.RoiLeft;
        var roiHeight = options.RoiBottom - options.RoiTop;
        var roi = new SKRectI(options.RoiLeft, options.RoiTop, options.RoiRight, options.RoiBottom);

        using var cropped = new SKBitmap(roiWidth, roiHeight);

        if (!source.ExtractSubset(cropped, roi))
        {
            _logger.LogWarning(
                "Sink-cleanliness ROI ({Roi}) falls outside the {Width}x{Height} capture; skipping.",
                roi, source.Width, source.Height);

            return null;
        }

        using var resized = new SKBitmap(InputSize, InputSize);
        cropped.ScalePixels(resized, SKSamplingOptions.Default);

        var input = ToTensor(resized);

        using var results = session.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
        var logits = results.First(r => r.Name == "logits").AsEnumerable<float>().ToArray();
        var probabilities = Softmax(logits);

        // Class order is ImageFolder's alphabetical sort in train.py
        // ("clean" < "dirty") - printed there at export time for the
        // operator to double-check the model matches this assumption.
        var isClean = probabilities[0] >= probabilities[1];

        return new SinkCleanlinessResult
        {
            IsClean = isClean,
            Confidence = isClean ? probabilities[0] : probabilities[1],
        };
    }

    private InferenceSession? LoadSession(string modelPath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, modelPath);

        try
        {
            return new InferenceSession(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load sink-cleanliness model at {Path}; classification disabled until this is fixed.",
                fullPath);

            return null;
        }
    }

    private static DenseTensor<float> ToTensor(SKBitmap bitmap)
    {
        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);

        for (var y = 0; y < InputSize; y++)
        {
            for (var x = 0; x < InputSize; x++)
            {
                var pixel = bitmap.GetPixel(x, y);

                tensor[0, 0, y, x] = (pixel.Red / 255f - Mean[0]) / Std[0];
                tensor[0, 1, y, x] = (pixel.Green / 255f - Mean[1]) / Std[1];
                tensor[0, 2, y, x] = (pixel.Blue / 255f - Mean[2]) / Std[2];
            }
        }

        return tensor;
    }

    private static float[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var exps = logits.Select(l => MathF.Exp(l - max)).ToArray();
        var sum = exps.Sum();

        return exps.Select(e => e / sum).ToArray();
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session?.Dispose();
    }
}
