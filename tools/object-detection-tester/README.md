# Object detection tester

Runs the real `ObjectDetector` (`Vivnest.Agent/Capabilities/Camera/ObjectDetector.cs`)
against a folder of photos, locally, with no device or deployment round-trip.
Prints every raw detection - no ROI filtering applied (that happens later,
in `SinkCleanlinessWorker`, not in `ObjectDetector` itself), so this is the
right tool for telling apart "the model isn't finding anything" from "it's
finding things outside the configured ROI."

## Usage

```
dotnet run --project tools/object-detection-tester -- <photos-folder> [confidence-threshold]
```

- `<photos-folder>` - a folder of `.jpg`/`.jpeg`/`.png` files. Put real
  downloaded captures here (see `.gitignore` below - never commit these).
- `[confidence-threshold]` - optional, defaults to `0.5` (same default as
  `ObjectDetectionOptions.ConfidenceThreshold`).

## Output

For each photo: its actual resolution, then every detection above the
threshold with class name, confidence, and box coordinates in the source
image's own pixel space - directly comparable against whatever ROI you've
configured (`ObjectDetectionOptions.RoiLeft/Top/Right/Bottom`).

## Not committed

Whatever you download into a `photos/` subfolder here - real home photos,
same reasoning as `scripts/ml/sink-cleanliness/raw/`+`dataset/` being
gitignored.
