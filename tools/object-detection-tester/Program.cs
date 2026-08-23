using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using Vivnest.Capabilities.AiClassification.Inference;
using Vivnest.Core.Options;

if (args.Length < 1)
{
    Console.WriteLine("Usage: dotnet run -- <photos-folder> [confidence-threshold]");
    return 1;
}

var photosFolder = args[0];

if (!Directory.Exists(photosFolder))
{
    Console.WriteLine($"Folder not found: {photosFolder}");
    return 1;
}

var confidenceThreshold = args.Length > 1 && double.TryParse(args[1], out var parsedThreshold)
    ? parsedThreshold
    : 0.5;

var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "object-detection", "yolov8n.onnx");

// Ground truth, not assumption - ObjectDetector.DecodeCandidates hardcodes
// [1, 84, N] (channels then anchors, the standard YOLOv8 export shape) as
// a documented assumption, never actually verified against this specific
// .onnx file. Printed once, up front, so a wrong axis order or an
// unexpected extra output (e.g. NMS baked into the export) shows up
// immediately instead of being silently misread as real detections.
Console.WriteLine("Model I/O (ground truth, not assumed):");

using (var diagnosticSession = new InferenceSession(modelPath))
{
    foreach (var (name, meta) in diagnosticSession.InputMetadata)
    {
        Console.WriteLine($"  input  '{name}': [{string.Join(", ", meta.Dimensions)}]");
    }

    foreach (var (name, meta) in diagnosticSession.OutputMetadata)
    {
        Console.WriteLine($"  output '{name}': [{string.Join(", ", meta.Dimensions)}]");
    }
}

Console.WriteLine();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
using var detector = new ObjectDetector(loggerFactory.CreateLogger<ObjectDetector>());

// No ROI here on purpose - ObjectDetector.Detect itself doesn't apply one;
// that filtering happens later, in AiClassificationWorker. Seeing every raw
// detection, in or out of wherever the real ROI is configured, is the
// entire point of this tool.
var options = new ObjectDetectionOptions
{
    Enabled = true,
    ModelPath = "Models/object-detection/yolov8n.onnx",
    ConfidenceThreshold = confidenceThreshold,
};

var photoFiles = Directory.EnumerateFiles(photosFolder)
    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f)
    .ToList();

if (photoFiles.Count == 0)
{
    Console.WriteLine($"No .jpg/.jpeg/.png files found in {photosFolder}");
    return 1;
}

Console.WriteLine($"Confidence threshold: {confidenceThreshold}");
Console.WriteLine($"Found {photoFiles.Count} photo(s).\n");

foreach (var file in photoFiles)
{
    var imageBytes = await File.ReadAllBytesAsync(file);

    using var bitmap = SKBitmap.Decode(imageBytes);
    var resolution = bitmap is null ? "unknown" : $"{bitmap.Width}x{bitmap.Height}";

    var detections = detector.Detect(imageBytes, options);

    Console.WriteLine($"{Path.GetFileName(file)} ({resolution}):");

    if (detections.Count == 0)
    {
        Console.WriteLine("  (no detections)");
    }
    else
    {
        foreach (var detection in detections.OrderByDescending(d => d.Confidence))
        {
            Console.WriteLine(
                $"  {detection.ClassName,-15} conf={detection.Confidence:F2}  box=({detection.X1},{detection.Y1})-({detection.X2},{detection.Y2})");
        }
    }

    Console.WriteLine();
}

return 0;
