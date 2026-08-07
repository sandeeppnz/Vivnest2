# Sink-cleanliness ML toolkit

One-off scripts, not part of the shipped product - see
[ADR-032](../../../docs/architecture/decision-log.md). `raw/` and
`dataset/` are gitignored; they hold real home photos.

```
pip install -r requirements.txt

set AZURE_STORAGE_CONNECTION_STRING=...   # from Vivnest.Agent's local config
python fetch_captures.py --tenant Sana --site 1Fitz --agent <agent-id> --camera camera-001

python label_tool.py
# c = clean, d = dirty, s = skip, q = quit - resumable, rerun any time

python train.py --dataset dataset --output ../../../Vivnest.Agent/Models/sink-cleanliness.onnx
```
