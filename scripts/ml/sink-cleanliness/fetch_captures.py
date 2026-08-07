"""Pull historical camera captures for one device out of Blob Storage.

One-off tool, not shipped with the product - see ADR-032. Blob name shape
comes from Vivnest.Infrastructure/Utils/BlobNameGenerator.cs:
    {tenant}/{site}/{agent}/{camera}/{yyyy}/{MM}/{dd}/{HH-mm-ss}.jpg

Usage:
    set AZURE_STORAGE_CONNECTION_STRING=...
    python fetch_captures.py --tenant Sana --site 1Fitz --agent <agent-id> --camera camera-001
"""
import argparse
import os
import sys
from pathlib import Path

from azure.storage.blob import ContainerClient


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--connection-string", default=os.environ.get("AZURE_STORAGE_CONNECTION_STRING"),
                         help="Defaults to AZURE_STORAGE_CONNECTION_STRING env var")
    parser.add_argument("--container", default="photos")
    parser.add_argument("--tenant", required=True)
    parser.add_argument("--site", required=True)
    parser.add_argument("--agent", required=True)
    parser.add_argument("--camera", default="camera-001")
    parser.add_argument("--output", default="raw")
    return parser.parse_args()


def local_filename(blob_name: str) -> str:
    # {tenant}/{site}/{agent}/{camera}/{yyyy}/{MM}/{dd}/{HH-mm-ss}.jpg
    # -> {yyyy}-{MM}-{dd}_{HH-mm-ss}.jpg, sortable and collision-free.
    parts = blob_name.split("/")
    yyyy, mm, dd, rest = parts[-4], parts[-3], parts[-2], parts[-1]
    return f"{yyyy}-{mm}-{dd}_{rest}"


def main() -> int:
    args = parse_args()

    if not args.connection_string:
        print("No connection string - pass --connection-string or set AZURE_STORAGE_CONNECTION_STRING.", file=sys.stderr)
        return 1

    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    prefix = f"{args.tenant}/{args.site}/{args.agent}/{args.camera}/"

    container = ContainerClient.from_connection_string(
        args.connection_string, container_name=args.container)

    downloaded = 0
    skipped = 0

    for blob in container.list_blobs(name_starts_with=prefix):
        dest = output_dir / local_filename(blob.name)

        if dest.exists():
            skipped += 1
            continue

        data = container.download_blob(blob).readall()
        dest.write_bytes(data)
        downloaded += 1

        if downloaded % 25 == 0:
            print(f"...{downloaded} downloaded")

    print(f"Done. Downloaded {downloaded}, already had {skipped}. Files in {output_dir.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
