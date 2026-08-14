#!/usr/bin/env python3

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path
from typing import TypeAlias

BencodeValue: TypeAlias = int | bytes | str | list["BencodeValue"] | dict[str, "BencodeValue"]
PIECE_LENGTH = 256 * 1024


def bencode(value: BencodeValue) -> bytes:
    if isinstance(value, int):
        return f"i{value}e".encode("ascii")
    if isinstance(value, str):
        value = value.encode("utf-8")
    if isinstance(value, bytes):
        return str(len(value)).encode("ascii") + b":" + value
    if isinstance(value, list):
        return b"l" + b"".join(bencode(item) for item in value) + b"e"
    if isinstance(value, dict):
        items = []
        for key in sorted(value):
            items.append(bencode(key))
            items.append(bencode(value[key]))
        return b"d" + b"".join(items) + b"e"
    raise TypeError(f"Unsupported bencode value: {type(value)!r}")


def write_payload(path: Path, size: int, seed: int) -> None:
    if path.exists() and path.stat().st_size == size:
        return

    path.parent.mkdir(parents=True, exist_ok=True)
    block = bytes((seed + offset) % 256 for offset in range(1024 * 1024))
    remaining = size
    with path.open("wb") as stream:
        while remaining:
            chunk = block[: min(len(block), remaining)]
            stream.write(chunk)
            remaining -= len(chunk)


def piece_hashes(path: Path) -> bytes:
    hashes = bytearray()
    with path.open("rb") as stream:
        while piece := stream.read(PIECE_LENGTH):
            hashes.extend(hashlib.sha1(piece).digest())  # noqa: S324 - required by BitTorrent v1
    return bytes(hashes)


def write_torrent(payload: Path, torrent_path: Path, use_webseed: bool) -> str:
    info: dict[str, BencodeValue] = {
        "length": payload.stat().st_size,
        "name": payload.name,
        "piece length": PIECE_LENGTH,
        "pieces": piece_hashes(payload),
    }
    torrent: dict[str, BencodeValue] = {
        "comment": "Deterministic local-only QControl fixture",
        "creation date": 0,
        "created by": "QControl test fixture generator",
        "info": info,
    }
    if use_webseed:
        torrent["url-list"] = f"http://webseed/{payload.name}"

    encoded = bencode(torrent)
    torrent_path.parent.mkdir(parents=True, exist_ok=True)
    torrent_path.write_bytes(encoded)
    return hashlib.sha1(bencode(info)).hexdigest()  # noqa: S324 - BitTorrent v1 info hash


def main() -> int:
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} OUTPUT_ROOT", file=sys.stderr)
        return 2

    output_root = Path(sys.argv[1]).resolve()
    webseed_root = output_root / "webseed"
    torrent_root = output_root / "torrents"
    definitions = [
        ("complete-seeding.bin", 1 * 1024 * 1024, 11, True),
        ("complete-stopped.bin", 1 * 1024 * 1024, 23, True),
        ("incomplete-stopped.bin", 2 * 1024 * 1024, 37, False),
        ("incomplete-stalled.bin", 2 * 1024 * 1024, 41, False),
        ("incomplete-downloading.bin", 32 * 1024 * 1024, 53, True),
        ("incomplete-queued.bin", 32 * 1024 * 1024, 67, True),
    ]

    manifest = []
    for name, size, seed, use_webseed in definitions:
        payload = webseed_root / name
        torrent_path = torrent_root / f"{name}.torrent"
        write_payload(payload, size, seed)
        info_hash = write_torrent(payload, torrent_path, use_webseed)
        manifest.append(
            {
                "name": name,
                "size": size,
                "infoHash": info_hash,
                "torrent": str(torrent_path.relative_to(output_root)),
                "webseed": use_webseed,
            }
        )

    (output_root / "manifest.json").write_text(
        json.dumps({"pieceLength": PIECE_LENGTH, "torrents": manifest}, indent=2) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
