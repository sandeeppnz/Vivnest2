# Sink-cleanliness tester

Runs the real `SinkCleanlinessClassifier`
(`Vivnest.Agent/Capabilities/Camera/SinkCleanlinessClassifier.cs`) against a
folder of photos, locally, with no device or deployment round-trip.

Unlike `ObjectDetector`, `SinkCleanlinessClassifier.Classify` *does* crop
to the ROI internally before classifying - the model was trained on
sink-area crops, not full frames, so an ROI has to be supplied for the
result to mean anything. It defaults to the real `camera-001` ROI
(`650,150,1300,650`) if you don't pass your own.

`ConfidenceThreshold` is not applied inside `Classify` itself - it always
returns its raw `IsClean`/`Confidence`. This tool derives the same
"effective" dirty/clean call `AiClassificationWorker` makes downstream
(`!IsClean && Confidence >= threshold`), and prints both the raw
classifier output and that effective call, so you can tell apart "the
model is uncertain" from "the model is confident but under threshold."

## Usage

```
dotnet run --project tools/sink-cleanliness-tester -- <photos-folder> [confidence-threshold] [roiLeft roiTop roiRight roiBottom]
```

- `<photos-folder>` - a folder of `.jpg`/`.jpeg`/`.png` files. Put real
  downloaded captures here (see `.gitignore` below - never commit these).
- `[confidence-threshold]` - optional, defaults to `0.6` (same default as
  `SinkCleanlinessOptions.ConfidenceThreshold`).
- `[roiLeft roiTop roiRight roiBottom]` - optional, all four or none;
  defaults to `camera-001`'s real configured ROI.

## Output

Model input/output node names and shapes (ground truth, not assumed -
`Classify` hardcodes an input node named `"input"` and an output node
named `"logits"`, never actually verified against this specific `.onnx`
file until this prints it), then for each photo: its actual resolution,
raw classifier output (`clean`/`dirty` + confidence), and the effective
dirty/clean call at the given threshold.

## Not committed

Whatever you download into a `photos/` subfolder here - real home
photos, same reasoning as `tools/object-detection-tester/photos/` and
`scripts/ml/sink-cleanliness/raw/`+`dataset/` being gitignored.
