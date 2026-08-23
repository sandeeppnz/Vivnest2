using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using Vivnest.Capabilities.AiClassification.Inference;
using Vivnest.Core.Options;

if (args.Length < 1)
{
    Console.WriteLine("Usage: dotnet run -- <photos-folder> [confidence-threshold] [roiLeft roiTop roiRight roiBottom]");
    Console.WriteLine("  confidence-threshold defaults to 0.6 (SinkCleanlinessOptions's own default)");
    Console.WriteLine("  ROI defaults to the real camera-001 config (650,150,1300,650) if not given");
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
    : 0.6;

int roiLeft = 0, roiTop = 0, roiRight = 0, roiBottom = 0;

var hasCustomRoi = args.Length >= 6
    && int.TryParse(args[2], out roiLeft)
    && int.TryParse(args[3], out roiTop)
    && int.TryParse(args[4], out roiRight)
    && int.TryParse(args[5], out roiBottom);

if (!hasCustomRoi)
{
    roiLeft = 650;
    roiTop = 150;
    roiRight = 1300;
    roiBottom = 650;
}

var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "sink-cleanliness.onnx");

// Ground truth, not assumption - SinkCleanlinessClassifier.Classify
// hardcodes the input node name "input" and reads an output node named
// "logits", assuming a 2-class (clean, dirty) head - never actually
// verified against this specific .onnx file. Printed once, up front, so
// a mismatched node name or an unexpected class count shows up
// immediately instead of silently producing wrong results.
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
using var classifier = new SinkCleanlinessClassifier(loggerFactory.CreateLogger<SinkCleanlinessClassifier>());

// Unlike ObjectDetector, SinkCleanlinessClassifier.Classify DOES crop to
// this ROI internally before classifying - the ROI isn't optional here,
// it's what makes the result mean anything (the model was trained on
// sink-area crops, not full frames). ConfidenceThreshold is NOT applied
// inside Classify itself though - it always returns its raw IsClean/
// Confidence, and it's whoever calls it (AiClassificationWorker in
// production, this tool below) that decides what to do with a threshold.
var options = new SinkCleanlinessOptions
{
    Enabled = true,
    ModelPath = "Models/sink-cleanliness.onnx",
    ConfidenceThreshold = confidenceThreshold,
    RoiLeft = roiLeft,
    RoiTop = roiTop,
    RoiRight = roiRight,
    RoiBottom = roiBottom,
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

Console.WriteLine($"ROI: ({roiLeft},{roiTop})-({roiRight},{roiBottom}){(hasCustomRoi ? "" : " (default, camera-001)")}");
Console.WriteLine($"Confidence threshold: {confidenceThreshold} (applied by this tool, not by Classify itself)");
Console.WriteLine($"Found {photoFiles.Count} photo(s).\n");

foreach (var file in photoFiles)
{
    var imageBytes = await File.ReadAllBytesAsync(file);

    using var bitmap = SKBitmap.Decode(imageBytes);
    var resolution = bitmap is null ? "unknown" : $"{bitmap.Width}x{bitmap.Height}";

    var result = classifier.Classify(imageBytes, options);

    Console.Write($"{Path.GetFileName(file)} ({resolution}): ");

    if (result is null)
    {
        Console.WriteLine("(could not classify - see log above for why)");
        continue;
    }

    // Same derivation AiClassificationWorker.ProcessAsync uses: a "dirty"
    // read only counts once it clears the confidence threshold, an
    // unconfident "maybe dirty" reads as clean.
    var isDirty = !result.IsClean && result.Confidence >= confidenceThreshold;
    var effective = isDirty ? "DIRTY" : "clean";

    Console.WriteLine(
        $"raw={(result.IsClean ? "clean" : "dirty")} confidence={result.Confidence:F3} -> effective={effective}");
}

return 0;
