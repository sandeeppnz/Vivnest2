using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using Vivnest.Abstractions;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Camera;

// YOLOv8 ONNX decode - see ADR-034's follow-up. Assumes the standard
// Ultralytics `yolo export model=yolov8n.pt format=onnx` shape: a single
// [1, 84, N] output (4 box coords + 80 COCO class scores per anchor, N
// anchors for a 640x640 input), no separate objectness score (v8 dropped
// it - the class score itself is the confidence) and no baked-in NMS. A
// different export (different input size, --nms baked in, a non-COCO or
// custom-trained model) would need different decode math here - this is
// not a generic ONNX object-detection decoder, it's specifically this
// export shape.
public sealed class ObjectDetector : IObjectDetector, IDisposable
{
    private const int InputSize = 640;
    private const float NmsIouThreshold = 0.45f;

    // Standard COCO 2017 class order (80 classes) - index here must match
    // the model's own class-score order exactly, since nothing in the
    // ONNX file itself names the classes.
    private static readonly string[] CocoClassNames =
    [
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
        "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat",
        "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack",
        "umbrella", "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball",
        "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket",
        "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
        "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair",
        "couch", "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse",
        "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink", "refrigerator",
        "book", "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush",
    ];

    private readonly ILogger<ObjectDetector> _logger;

    // Keyed by ModelPath, same reasoning as SinkCleanlinessClassifier's
    // own session cache - a null value means loading already failed for
    // that path, cached too so a missing/bad model logs once at startup
    // instead of once per capture.
    private readonly ConcurrentDictionary<string, InferenceSession?> _sessions = new();

    public ObjectDetector(ILogger<ObjectDetector> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<Detection> Detect(byte[] imageBytes, ObjectDetectionOptions options)
    {
        var session = _sessions.GetOrAdd(options.ModelPath, LoadSession);

        if (session is null)
            return [];

        using var source = SKBitmap.Decode(imageBytes);

        if (source is null)
        {
            _logger.LogWarning("Could not decode capture for object detection.");
            return [];
        }

        // Full frame, not cropped to the ROI first - unlike the sink
        // classifier, YOLO expects normal scene context and the ROI is
        // applied afterward as a filter on the resulting boxes instead.
        // Stretched (not letterboxed) to the model's square input, same
        // simplification SinkCleanlinessClassifier already makes.
        // Rgba8888 explicitly (not the platform default, which varies) so
        // ToTensor can read the raw byte buffer directly instead of calling
        // GetPixel() per pixel - see ToTensor's own comment for why that
        // matters.
        using var resized = new SKBitmap(new SKImageInfo(InputSize, InputSize, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        if (!source.ScalePixels(resized, SKSamplingOptions.Default))
        {
            _logger.LogWarning(
                "ScalePixels failed to resize the capture to {Size}x{Size} for object detection; skipping.",
                InputSize, InputSize);

            return [];
        }

        var input = ToTensor(resized);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var values = input.ToArray();
            _logger.LogDebug(
                "Object-detection input tensor: min={Min:F3} max={Max:F3} mean={Mean:F3}",
                values.Min(), values.Max(), values.Average());
        }

        var inputName = session.InputMetadata.Keys.First();

        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, input)]);
        var output = results.First().AsTensor<float>();

        var scaleX = (float)source.Width / InputSize;
        var scaleY = (float)source.Height / InputSize;

        var candidates = DecodeCandidates(output, options.ConfidenceThreshold, scaleX, scaleY);

        return NonMaxSuppress(candidates);
    }

    private static List<Detection> DecodeCandidates(
        Tensor<float> output,
        double confidenceThreshold,
        float scaleX,
        float scaleY)
    {
        // output: [1, 84, numAnchors] - channel 0-3 are box coords
        // (cx,cy,w,h in model-input pixel space), channels 4-83 are the 80
        // class scores for that anchor.
        var numAnchors = output.Dimensions[2];
        var candidates = new List<Detection>();

        for (var anchor = 0; anchor < numAnchors; anchor++)
        {
            var bestClassIndex = -1;
            var bestScore = 0f;

            for (var classIndex = 0; classIndex < CocoClassNames.Length; classIndex++)
            {
                var score = output[0, 4 + classIndex, anchor];

                if (score > bestScore)
                {
                    bestScore = score;
                    bestClassIndex = classIndex;
                }
            }

            if (bestClassIndex < 0 || bestScore < confidenceThreshold)
                continue;

            var cx = output[0, 0, anchor] * scaleX;
            var cy = output[0, 1, anchor] * scaleY;
            var w = output[0, 2, anchor] * scaleX;
            var h = output[0, 3, anchor] * scaleY;

            candidates.Add(new Detection
            {
                ClassName = CocoClassNames[bestClassIndex],
                Confidence = bestScore,
                X1 = (int)(cx - w / 2),
                Y1 = (int)(cy - h / 2),
                X2 = (int)(cx + w / 2),
                Y2 = (int)(cy + h / 2),
            });
        }

        return candidates;
    }

    // Greedy NMS, per class - highest-confidence box wins, anything else
    // of the same class overlapping it past the IoU threshold is dropped.
    // Different classes never suppress each other (a person and a cup can
    // legitimately overlap in frame).
    private static List<Detection> NonMaxSuppress(List<Detection> candidates)
    {
        var kept = new List<Detection>();

        foreach (var group in candidates.GroupBy(d => d.ClassName))
        {
            var remaining = group.OrderByDescending(d => d.Confidence).ToList();

            while (remaining.Count > 0)
            {
                var best = remaining[0];
                kept.Add(best);
                remaining.RemoveAt(0);
                remaining.RemoveAll(d => IoU(best, d) > NmsIouThreshold);
            }
        }

        return kept;
    }

    private static float IoU(Detection a, Detection b)
    {
        var x1 = Math.Max(a.X1, b.X1);
        var y1 = Math.Max(a.Y1, b.Y1);
        var x2 = Math.Min(a.X2, b.X2);
        var y2 = Math.Min(a.Y2, b.Y2);

        var intersection = (float)Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);

        var areaA = (float)(a.X2 - a.X1) * (a.Y2 - a.Y1);
        var areaB = (float)(b.X2 - b.X1) * (b.Y2 - b.Y1);

        var union = areaA + areaB - intersection;

        return union <= 0 ? 0 : intersection / union;
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
                "Failed to load object-detection model at {Path}; detection disabled until this is fixed.",
                fullPath);

            return null;
        }
    }

    private static DenseTensor<float> ToTensor(SKBitmap bitmap)
    {
        // Standard Ultralytics export expects RGB scaled to [0,1], no
        // ImageNet mean/std normalization (unlike the sink classifier's
        // torchvision-style preprocessing) - channels-first (CHW).
        //
        // One bulk copy of the whole pixel buffer, not 409,600 individual
        // GetPixel() calls (640x640) - each GetPixel is its own native
        // interop round-trip, and on a Raspberry Pi that added up to enough
        // wall-clock time per capture to starve the .NET thread pool and
        // stall CameraCaptureWorker's motion-triggered burst cadence (found
        // live, not theoretically - see ADR-034's follow-up). Requires the
        // bitmap to actually be Rgba8888 (forced when it's constructed
        // above) - Bytes is whatever raw format the bitmap holds, and
        // reading it as Rgba8888 against a different underlying format
        // would silently scramble every channel.
        var pixels = bitmap.Bytes;
        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);

        for (var y = 0; y < InputSize; y++)
        {
            var rowOffset = y * InputSize * 4;

            for (var x = 0; x < InputSize; x++)
            {
                var pixelOffset = rowOffset + x * 4;

                tensor[0, 0, y, x] = pixels[pixelOffset] / 255f;
                tensor[0, 1, y, x] = pixels[pixelOffset + 1] / 255f;
                tensor[0, 2, y, x] = pixels[pixelOffset + 2] / 255f;
            }
        }

        return tensor;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session?.Dispose();
    }
}
