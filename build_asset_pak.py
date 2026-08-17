from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from asset_pak import ASSET_NAMES, AssetPakError, load_asset_pak, write_asset_pak


ROOT = Path(__file__).resolve().parent
SOURCE_ROOT = ROOT / "assets"
DEFAULT_PAK_PATH = SOURCE_ROOT / "characters" / "angelis.pak"


def backup_sources(source_root: Path, backup_root: Path) -> None:
    backup_root.mkdir(parents=True, exist_ok=True)
    manifest: list[str] = []
    for name in ASSET_NAMES:
        source = (source_root / name).read_bytes()
        destination = backup_root / name
        if destination.exists() and destination.read_bytes() != source:
            raise AssetPakError(f"Existing backup differs: {destination}")
        destination.write_bytes(source)
        manifest.append(f"{hashlib.sha256(source).hexdigest()}  {name}")
    (backup_root / "SHA256SUMS.txt").write_text("\n".join(manifest) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Build the encrypted assets.pak bundle")
    parser.add_argument(
        "--source-dir",
        type=Path,
        default=SOURCE_ROOT,
        help="Directory containing the source PNGs (default: assets)",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_PAK_PATH,
        help="Destination .pak path (default: assets/characters/angelis.pak)",
    )
    parser.add_argument("--backup-dir", type=Path, help="Directory that receives original PNG backups")
    parser.add_argument("--delete-source", action="store_true", help="Delete PNGs after all checks pass")
    args = parser.parse_args()

    if args.delete_source and args.backup_dir is None:
        raise SystemExit("--delete-source requires --backup-dir")

    source_root = args.source_dir.resolve()
    pak_path = args.output.resolve()
    if args.delete_source and source_root != SOURCE_ROOT.resolve():
        raise SystemExit("--delete-source is allowed only for the default assets source directory")

    if args.backup_dir is not None:
        backup_sources(source_root, args.backup_dir.resolve())
        print(f"backup verified: {args.backup_dir.resolve()}")

    pak_path.parent.mkdir(parents=True, exist_ok=True)
    write_asset_pak(source_root, pak_path)
    loaded = load_asset_pak(pak_path.read_bytes())
    print(f"package verified: {pak_path} ({len(loaded)} assets)")

    if args.delete_source:
        for name in ASSET_NAMES:
            (source_root / name).unlink()
            print(f"removed source copy: {name}")


if __name__ == "__main__":
    main()
