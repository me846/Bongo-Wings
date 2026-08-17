from __future__ import annotations

import hashlib
import hmac
import json
import os
import zipfile
from io import BytesIO
from pathlib import Path


PAK_MAGIC = b"ABPAK03\0"
SALT_SIZE = 16
NONCE_SIZE = 16
TAG_SIZE = 32
KEY_PARTS = (
    bytes.fromhex("d48f26ac5c3419e3b70d92f641a8750e"),
    bytes.fromhex("7b135d0aeb6284c9f036a7519ce247bd"),
    bytes.fromhex("49a203fdd8916e5b27c04a73be1598f6"),
)
ASSET_NAMES = (
    "bg1.png",
    "bg2.png",
    "bg3.png",
    "bg4.png",
    "hands.png",
    "handsLiver.png",
    "Liver.png",
)


class AssetPakError(RuntimeError):
    pass


def _derive_keys(salt: bytes) -> tuple[bytes, bytes]:
    master = hashlib.sha256(b"".join(KEY_PARTS)).digest()
    cipher_key = hmac.digest(master, b"media-cipher\0" + salt, "sha256")
    auth_key = hmac.digest(master, b"media-auth\0" + salt, "sha256")
    return cipher_key, auth_key


def _xor_stream(data: bytes, key: bytes, nonce: bytes) -> bytes:
    result = bytearray(len(data))
    offset = 0
    counter = 0
    while offset < len(data):
        block = hmac.digest(
            key,
            b"media-stream\0" + nonce + counter.to_bytes(8, "big"),
            "sha256",
        )
        length = min(len(block), len(data) - offset)
        for index in range(length):
            result[offset + index] = data[offset + index] ^ block[index]
        offset += length
        counter += 1
    return bytes(result)


def _encrypt(data: bytes) -> bytes:
    salt = os.urandom(SALT_SIZE)
    nonce = os.urandom(NONCE_SIZE)
    cipher_key, auth_key = _derive_keys(salt)
    ciphertext = _xor_stream(data, cipher_key, nonce)
    authenticated = PAK_MAGIC + salt + nonce + ciphertext
    tag = hmac.digest(auth_key, authenticated, "sha256")
    return authenticated + tag


def _decrypt(protected: bytes) -> bytes:
    minimum_size = len(PAK_MAGIC) + SALT_SIZE + NONCE_SIZE + TAG_SIZE
    if len(protected) < minimum_size or not protected.startswith(PAK_MAGIC):
        raise AssetPakError("Invalid media package header.")

    salt_start = len(PAK_MAGIC)
    nonce_start = salt_start + SALT_SIZE
    data_start = nonce_start + NONCE_SIZE
    salt = protected[salt_start:nonce_start]
    nonce = protected[nonce_start:data_start]
    ciphertext = protected[data_start:-TAG_SIZE]
    supplied_tag = protected[-TAG_SIZE:]
    cipher_key, auth_key = _derive_keys(salt)
    expected_tag = hmac.digest(auth_key, protected[:-TAG_SIZE], "sha256")
    if not hmac.compare_digest(supplied_tag, expected_tag):
        raise AssetPakError("Media package integrity check failed.")
    return _xor_stream(ciphertext, cipher_key, nonce)


def build_asset_pak(source_root: Path) -> bytes:
    assets: dict[str, bytes] = {}
    for name in ASSET_NAMES:
        path = source_root / name
        if not path.is_file():
            raise AssetPakError(f"Missing source asset: {path}")
        data = path.read_bytes()
        if not data.startswith(b"\x89PNG\r\n\x1a\n"):
            raise AssetPakError(f"Not a PNG file: {path}")
        assets[name] = data

    manifest = {
        "format": 1,
        "assets": {
            name: {"sha256": hashlib.sha256(data).hexdigest(), "size": len(data)}
            for name, data in assets.items()
        },
    }
    archive_buffer = BytesIO()
    with zipfile.ZipFile(archive_buffer, "w", compression=zipfile.ZIP_STORED) as archive:
        archive.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False, sort_keys=True))
        for name, data in assets.items():
            archive.writestr(name, data)

    return _encrypt(archive_buffer.getvalue())


def load_asset_pak(protected: bytes) -> dict[str, bytes]:
    archive_bytes = _decrypt(protected)
    try:
        with zipfile.ZipFile(BytesIO(archive_bytes), "r") as archive:
            manifest = json.loads(archive.read("manifest.json"))
            if manifest.get("format") != 1:
                raise AssetPakError("Unsupported assets.pak version.")
            assets = {name: archive.read(name) for name in ASSET_NAMES}
    except (KeyError, OSError, ValueError, zipfile.BadZipFile, json.JSONDecodeError) as error:
        raise AssetPakError(f"Invalid assets.pak contents: {error}") from error

    for name, data in assets.items():
        entry = manifest.get("assets", {}).get(name, {})
        if entry.get("size") != len(data) or entry.get("sha256") != hashlib.sha256(data).hexdigest():
            raise AssetPakError(f"Asset integrity check failed: {name}")
    return assets


def write_asset_pak(source_root: Path, destination: Path) -> None:
    protected = build_asset_pak(source_root)

    load_asset_pak(protected)
    temporary = destination.with_suffix(destination.suffix + ".tmp")
    temporary.write_bytes(protected)
    os.replace(temporary, destination)
