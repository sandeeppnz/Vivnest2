"""Quick keyboard-driven Clean/NotClean labeling over images in raw/.

Sorts each image into dataset/clean/ or dataset/dirty/ - a plain
ImageFolder-compatible layout that train.py (Phase 2) reads directly.
Resumable: already-labeled filenames are skipped on rerun.

Keys: c = clean, d = dirty, s = skip (leave unlabeled for now), q = quit.

Usage:
    python label_tool.py --input raw --output dataset
"""
import argparse
import shutil
from pathlib import Path

import cv2

MAX_DISPLAY_DIM = 900


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", default="raw")
    parser.add_argument("--output", default="dataset")
    return parser.parse_args()


def already_labeled(filename: str, clean_dir: Path, dirty_dir: Path) -> bool:
    return (clean_dir / filename).exists() or (dirty_dir / filename).exists()


def display_image(image):
    h, w = image.shape[:2]
    scale = MAX_DISPLAY_DIM / max(h, w)

    if scale < 1:
        image = cv2.resize(image, (int(w * scale), int(h * scale)))

    cv2.imshow("label (c=clean d=dirty s=skip q=quit)", image)


def main() -> int:
    args = parse_args()

    input_dir = Path(args.input)
    clean_dir = Path(args.output) / "clean"
    dirty_dir = Path(args.output) / "dirty"
    clean_dir.mkdir(parents=True, exist_ok=True)
    dirty_dir.mkdir(parents=True, exist_ok=True)

    files = sorted(p for p in input_dir.iterdir() if p.suffix.lower() in (".jpg", ".jpeg", ".png"))
    pending = [p for p in files if not already_labeled(p.name, clean_dir, dirty_dir)]

    if not pending:
        print("Nothing left to label.")
        return 0

    print(f"{len(pending)} of {len(files)} images need labeling.")

    for i, path in enumerate(pending, start=1):
        image = cv2.imread(str(path))

        if image is None:
            print(f"Skipping unreadable file: {path.name}")
            continue

        print(f"[{i}/{len(pending)}] {path.name}")
        display_image(image)

        while True:
            key = cv2.waitKey(0) & 0xFF

            if key == ord("c"):
                shutil.copy2(path, clean_dir / path.name)
                break
            elif key == ord("d"):
                shutil.copy2(path, dirty_dir / path.name)
                break
            elif key == ord("s"):
                break
            elif key == ord("q"):
                cv2.destroyAllWindows()
                print("Stopped early.")
                return 0

    cv2.destroyAllWindows()
    print("Done labeling this batch.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
