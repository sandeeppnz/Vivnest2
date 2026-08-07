"""Fine-tune MobileNetV3-Small on dataset/{clean,dirty}/ and export to ONNX.

One-off training script, not part of the shipped product - see ADR-032.
Runs outside the .NET solution; the .onnx it produces is what
Vivnest.Agent actually loads at runtime.

Usage:
    python train.py --dataset dataset --output ../../../Vivnest.Agent/Models/sink-cleanliness.onnx
"""
import argparse

import torch
from torch import nn
from torch.utils.data import DataLoader, random_split
from torchvision import transforms
from torchvision.datasets import ImageFolder
from torchvision.models import MobileNet_V3_Small_Weights, mobilenet_v3_small

INPUT_SIZE = 224
IMAGENET_MEAN = [0.485, 0.456, 0.406]
IMAGENET_STD = [0.229, 0.224, 0.225]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset", default="dataset")
    parser.add_argument("--epochs", type=int, default=15)
    parser.add_argument("--batch-size", type=int, default=16)
    parser.add_argument("--lr", type=float, default=1e-4)
    parser.add_argument("--val-split", type=float, default=0.2)
    parser.add_argument("--output", default="sink-cleanliness.onnx")
    return parser.parse_args()


def build_datasets(root: str, val_split: float):
    train_transform = transforms.Compose([
        transforms.Resize((INPUT_SIZE, INPUT_SIZE)),
        transforms.RandomHorizontalFlip(),
        transforms.ToTensor(),
        transforms.Normalize(IMAGENET_MEAN, IMAGENET_STD),
    ])
    val_transform = transforms.Compose([
        transforms.Resize((INPUT_SIZE, INPUT_SIZE)),
        transforms.ToTensor(),
        transforms.Normalize(IMAGENET_MEAN, IMAGENET_STD),
    ])

    full = ImageFolder(root, transform=train_transform)
    val_size = max(1, int(len(full) * val_split))
    train_size = len(full) - val_size

    train_set, val_set = random_split(full, [train_size, val_size])

    # random_split's indices point into `full`; swapping the underlying dataset
    # for a second ImageFolder instance (no augmentation) keeps those indices
    # valid, since ImageFolder scans class/filename order deterministically.
    val_set.dataset = ImageFolder(root, transform=val_transform)

    return train_set, val_set, full.classes


def build_model() -> nn.Module:
    model = mobilenet_v3_small(weights=MobileNet_V3_Small_Weights.DEFAULT)
    in_features = model.classifier[-1].in_features
    model.classifier[-1] = nn.Linear(in_features, 2)
    return model


def run_epoch(model, loader, device, optimizer=None) -> tuple[float, float]:
    training = optimizer is not None
    model.train(training)

    total_loss = 0.0
    correct = 0
    count = 0
    loss_fn = nn.CrossEntropyLoss()

    with torch.set_grad_enabled(training):
        for images, labels in loader:
            images, labels = images.to(device), labels.to(device)
            logits = model(images)
            loss = loss_fn(logits, labels)

            if training:
                optimizer.zero_grad()
                loss.backward()
                optimizer.step()

            total_loss += loss.item() * images.size(0)
            correct += (logits.argmax(dim=1) == labels).sum().item()
            count += images.size(0)

    return total_loss / count, correct / count


def main() -> None:
    args = parse_args()
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    train_set, val_set, classes = build_datasets(args.dataset, args.val_split)
    print(f"Classes (index order, matters for reading logits later): {classes}")
    print(f"Train: {len(train_set)}, Val: {len(val_set)}")

    train_loader = DataLoader(train_set, batch_size=args.batch_size, shuffle=True)
    val_loader = DataLoader(val_set, batch_size=args.batch_size)

    model = build_model().to(device)
    optimizer = torch.optim.Adam(model.parameters(), lr=args.lr)

    for epoch in range(1, args.epochs + 1):
        train_loss, train_acc = run_epoch(model, train_loader, device, optimizer)
        val_loss, val_acc = run_epoch(model, val_loader, device)
        print(f"Epoch {epoch}/{args.epochs}  train_loss={train_loss:.4f} train_acc={train_acc:.3f}  "
              f"val_loss={val_loss:.4f} val_acc={val_acc:.3f}")

    print(f"Final val_acc={val_acc:.3f} on {len(val_set)} held-out images - "
          "judge for yourself whether that's trustworthy before deploying this.")

    model.eval().to("cpu")
    dummy_input = torch.zeros(1, 3, INPUT_SIZE, INPUT_SIZE)

    torch.onnx.export(
        model,
        dummy_input,
        args.output,
        input_names=["input"],
        output_names=["logits"],
    )

    print(f"Exported {args.output}. Class order was {classes} - "
          "index 0 is the higher-confidence class only if 'clean' sorted first, "
          "verify against the printed class list above before wiring up ConfidenceThreshold logic.")


if __name__ == "__main__":
    main()
