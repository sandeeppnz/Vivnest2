# Models

Trained `.onnx` model files the Agent loads at runtime go here (referenced
by `DeviceOptions.SinkCleanliness.ModelPath`, relative to this folder -
see ADR-032). Not produced by this build; produced by
`scripts/ml/sink-cleanliness/train.py` and dropped in manually once
trained.

`object-detection/yolov8n.onnx` - referenced by
`DeviceOptions.ObjectDetection.ModelPath` (ADR-034's follow-up). Not
custom-trained - the standard Ultralytics YOLOv8n export
(`yolo export model=yolov8n.pt format=onnx`), COCO-pretrained. `ObjectDetector`
assumes this exact export shape (`[1, 84, N]`, no baked-in NMS); a
different YOLO version or export flags would need different decode math.
AGPL-3.0 licensed (Ultralytics) - worth knowing before distributing
anything built on it beyond personal use.
