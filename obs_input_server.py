from __future__ import annotations

import argparse
import configparser
import ctypes
import json
import os
import socket
import sys
import threading
from collections.abc import Callable
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from io import BytesIO
from pathlib import Path
from urllib.parse import unquote, urlparse

from asset_pak import ASSET_NAMES, AssetPakError, load_asset_pak

try:
    from PIL import Image, ImageChops
except ImportError:
    Image = None
    ImageChops = None


FROZEN = bool(getattr(sys, "frozen", False))
ROOT = Path(sys.executable).resolve().parent if FROZEN else Path(__file__).resolve().parent
WEB_ROOT = ROOT / "web" if FROZEN else ROOT
CONFIG_PATH = ROOT / "config.ini"
DEFAULT_CHARACTER = "angelis"
CHARACTER_PAK_ROOT = (
    WEB_ROOT / "characters" if FROZEN else ROOT / "assets" / "characters"
)
ASSET_ROUTES = {
    "bg1": "bg1.png",
    "bg2": "bg2.png",
    "bg3": "bg3.png",
    "bg4": "bg4.png",
    "hands": "hands.png",
    "hands-lever": "handsLiver.png",
    "lever": "Liver.png",
}
CHROMA_KEY_TOLERANCE = 8


def build_key_map() -> dict[str, int]:
    key_map: dict[str, int] = {}

    for letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
        key_map[f"Key{letter}"] = ord(letter)
    for digit in "0123456789":
        key_map[f"Digit{digit}"] = ord(digit)
    for number in range(1, 25):
        key_map[f"F{number}"] = 0x6F + number
    for number in range(10):
        key_map[f"Numpad{number}"] = 0x60 + number

    key_map.update(
        {
            "Backspace": 0x08,
            "Tab": 0x09,
            "Enter": 0x0D,
            "ShiftLeft": 0xA0,
            "ShiftRight": 0xA1,
            "ControlLeft": 0xA2,
            "ControlRight": 0xA3,
            "AltLeft": 0xA4,
            "AltRight": 0xA5,
            "Pause": 0x13,
            "CapsLock": 0x14,
            "Escape": 0x1B,
            "Space": 0x20,
            "PageUp": 0x21,
            "PageDown": 0x22,
            "End": 0x23,
            "Home": 0x24,
            "ArrowLeft": 0x25,
            "ArrowUp": 0x26,
            "ArrowRight": 0x27,
            "ArrowDown": 0x28,
            "PrintScreen": 0x2C,
            "Insert": 0x2D,
            "Delete": 0x2E,
            "MetaLeft": 0x5B,
            "MetaRight": 0x5C,
            "NumpadMultiply": 0x6A,
            "NumpadAdd": 0x6B,
            "NumpadSubtract": 0x6D,
            "NumpadDecimal": 0x6E,
            "NumpadDivide": 0x6F,
            "NumLock": 0x90,
            "ScrollLock": 0x91,
            "Semicolon": 0xBA,
            "Equal": 0xBB,
            "Comma": 0xBC,
            "Minus": 0xBD,
            "Period": 0xBE,
            "Slash": 0xBF,
            "Backquote": 0xC0,
            "BracketLeft": 0xDB,
            "Backslash": 0xDC,
            "BracketRight": 0xDD,
            "Quote": 0xDE,
        }
    )
    return key_map


KEY_CODE_TO_VK = build_key_map()

BINDING_DEFINITIONS = {
    "direction-up": ("Lever", "Up", ["ArrowUp", "KeyW"]),
    "direction-down": ("Lever", "Down", ["ArrowDown", "KeyS"]),
    "direction-left": ("Lever", "Left", ["ArrowLeft", "KeyA"]),
    "direction-right": ("Lever", "Right", ["ArrowRight", "KeyD"]),
    "button-4": ("UpperButtons", "Button1", ["KeyU"]),
    "button-5": ("UpperButtons", "Button2", ["KeyI"]),
    "button-6": ("UpperButtons", "Button3", ["KeyO"]),
    "button-7": ("UpperButtons", "Button4", ["KeyP"]),
    "button-0": ("LowerButtons", "Button1", ["KeyJ"]),
    "button-1": ("LowerButtons", "Button2", ["KeyK"]),
    "button-2": ("LowerButtons", "Button3", ["KeyL"]),
    "button-3": ("LowerButtons", "Button4", ["Semicolon"]),
}

GAMEPAD_BINDING_DEFINITIONS = {
    "direction-up": ("GamepadLever", "Up", ["Button12", "Axis1-"]),
    "direction-down": ("GamepadLever", "Down", ["Button13", "Axis1+"]),
    "direction-left": ("GamepadLever", "Left", ["Button14", "Axis0-"]),
    "direction-right": ("GamepadLever", "Right", ["Button15", "Axis0+"]),
    "button-4": ("GamepadUpperButtons", "Button1", ["Button2"]),
    "button-5": ("GamepadUpperButtons", "Button2", ["Button3"]),
    "button-6": ("GamepadUpperButtons", "Button3", ["Button5"]),
    "button-7": ("GamepadUpperButtons", "Button4", ["Button4"]),
    "button-0": ("GamepadLowerButtons", "Button1", ["Button0"]),
    "button-1": ("GamepadLowerButtons", "Button2", ["Button1"]),
    "button-2": ("GamepadLowerButtons", "Button3", ["Button7"]),
    "button-3": ("GamepadLowerButtons", "Button4", ["Button6"]),
}


class IniBindingStore:
    def __init__(self, path: Path, character_ids: Callable[[], tuple[str, ...]]) -> None:
        self.path = path
        self.character_ids = character_ids
        self._lock = threading.Lock()
        self._last_modified_ns = -1
        self._character = DEFAULT_CHARACTER
        self._keyboard_bindings = self._defaults(BINDING_DEFINITIONS)
        self._gamepad_bindings = self._defaults(GAMEPAD_BINDING_DEFINITIONS)

    @staticmethod
    def _defaults(definitions: dict[str, tuple[str, str, list[str]]]) -> dict[str, list[str]]:
        return {
            action_id: defaults.copy()
            for action_id, (_, _, defaults) in definitions.items()
        }

    def _write_defaults(self) -> None:
        default_character = self._default_character()
        lines = [
            "; Pseudo-3D lever input bindings",
            "; KeyboardEvent.code, ButtonN and AxisN+/- values may be comma-separated.",
            "",
            "[Meta]",
            "Version=3",
            "",
            "[Display]",
            f"Character={default_character}",
        ]
        current_section = ""
        for definitions in (BINDING_DEFINITIONS, GAMEPAD_BINDING_DEFINITIONS):
            for _, (section, ini_key, defaults) in definitions.items():
                if section != current_section:
                    current_section = section
                    lines.extend(["", f"[{section}]"])
                lines.append(f"{ini_key}={','.join(defaults)}")
        self.path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    def get(self) -> tuple[str, dict[str, list[str]], dict[str, list[str]]]:
        with self._lock:
            if not self.path.exists():
                self._write_defaults()

            modified_ns = self.path.stat().st_mtime_ns
            if modified_ns == self._last_modified_ns:
                self._character = self._validated_character(self._character)
                return self._copies()

            parser = configparser.ConfigParser(interpolation=None)
            try:
                parser.read(self.path, encoding="utf-8")
                selected_character = parser.get(
                    "Display", "Character", fallback=DEFAULT_CHARACTER
                ).strip().casefold()
                self._character = self._validated_character(selected_character)
                self._keyboard_bindings = self._load_definitions(parser, BINDING_DEFINITIONS)
                self._gamepad_bindings = self._load_definitions(
                    parser, GAMEPAD_BINDING_DEFINITIONS
                )
                self._last_modified_ns = modified_ns
            except (OSError, configparser.Error):


                pass

            return self._copies()

    def _default_character(self) -> str:
        available = self.character_ids()
        if DEFAULT_CHARACTER in available:
            return DEFAULT_CHARACTER
        return available[0] if available else DEFAULT_CHARACTER

    def _validated_character(self, character: str) -> str:
        normalized = character.casefold()
        return normalized if normalized in self.character_ids() else self._default_character()

    @staticmethod
    def _load_definitions(
        parser: configparser.ConfigParser,
        definitions: dict[str, tuple[str, str, list[str]]],
    ) -> dict[str, list[str]]:
        loaded: dict[str, list[str]] = {}
        for action_id, (section, ini_key, defaults) in definitions.items():
            raw_value = parser.get(section, ini_key, fallback=",".join(defaults))
            loaded[action_id] = list(
                dict.fromkeys(
                    value.strip()
                    for value in raw_value.split(",")
                    if value.strip()
                )
            )
        return loaded

    def _copies(self) -> tuple[str, dict[str, list[str]], dict[str, list[str]]]:
        return (
            self._character,
            {key: value.copy() for key, value in self._keyboard_bindings.items()},
            {key: value.copy() for key, value in self._gamepad_bindings.items()},
        )


class XInputGamepad(ctypes.Structure):
    _fields_ = [
        ("buttons", ctypes.c_ushort),
        ("left_trigger", ctypes.c_ubyte),
        ("right_trigger", ctypes.c_ubyte),
        ("thumb_lx", ctypes.c_short),
        ("thumb_ly", ctypes.c_short),
        ("thumb_rx", ctypes.c_short),
        ("thumb_ry", ctypes.c_short),
    ]


class XInputState(ctypes.Structure):
    _fields_ = [("packet_number", ctypes.c_ulong), ("gamepad", XInputGamepad)]


class WindowsInputReader:
    def __init__(self, binding_store: IniBindingStore) -> None:
        if os.name != "nt":
            raise RuntimeError("OBS input relay currently supports Windows only.")

        self.user32 = ctypes.WinDLL("user32", use_last_error=True)
        self.user32.GetAsyncKeyState.argtypes = [ctypes.c_int]
        self.user32.GetAsyncKeyState.restype = ctypes.c_short
        self.xinput = self._load_xinput()
        self.binding_store = binding_store

    @staticmethod
    def _load_xinput():
        for library_name in ("xinput1_4", "xinput9_1_0", "xinput1_3"):
            try:
                library = ctypes.WinDLL(library_name)
                library.XInputGetState.argtypes = [ctypes.c_ulong, ctypes.POINTER(XInputState)]
                library.XInputGetState.restype = ctypes.c_ulong
                return library
            except OSError:
                continue
        return None

    def pressed_keys(self) -> list[str]:
        return [
            code
            for code, virtual_key in KEY_CODE_TO_VK.items()
            if self.user32.GetAsyncKeyState(virtual_key) & 0x8000
        ]

    @staticmethod
    def _axis(value: int) -> float:
        divisor = 32767.0 if value >= 0 else 32768.0
        return round(max(-1.0, min(1.0, value / divisor)), 5)

    def gamepad_state(self) -> dict[str, object]:
        if self.xinput is None:
            return {"connected": False}

        for user_index in range(4):
            state = XInputState()
            if self.xinput.XInputGetState(user_index, ctypes.byref(state)) != 0:
                continue

            pad = state.gamepad
            masks = [
                0x1000,
                0x2000,
                0x4000,
                0x8000,
                0x0100,
                0x0200,
            ]
            buttons = [1.0 if pad.buttons & mask else 0.0 for mask in masks]
            buttons.extend(
                [
                    round(pad.left_trigger / 255.0, 5),
                    round(pad.right_trigger / 255.0, 5),
                    1.0 if pad.buttons & 0x0020 else 0.0,
                    1.0 if pad.buttons & 0x0010 else 0.0,
                    1.0 if pad.buttons & 0x0040 else 0.0,
                    1.0 if pad.buttons & 0x0080 else 0.0,
                    1.0 if pad.buttons & 0x0001 else 0.0,
                    1.0 if pad.buttons & 0x0002 else 0.0,
                    1.0 if pad.buttons & 0x0004 else 0.0,
                    1.0 if pad.buttons & 0x0008 else 0.0,
                ]
            )

            return {
                "connected": True,
                "id": f"XInput Controller {user_index + 1}",
                "axes": [
                    self._axis(pad.thumb_lx),
                    -self._axis(pad.thumb_ly),
                    self._axis(pad.thumb_rx),
                    -self._axis(pad.thumb_ry),
                ],
                "buttons": buttons,
            }

        return {"connected": False}

    def state(self) -> dict[str, object]:
        character, keyboard_bindings, gamepad_bindings = self.binding_store.get()
        return {
            "character": character,
            "keys": self.pressed_keys(),
            "gamepad": self.gamepad_state(),
            "bindings": keyboard_bindings,
            "gamepadBindings": gamepad_bindings,
        }


class ChromaAssetStore:


    def __init__(self, pak_root: Path) -> None:
        self.pak_root = pak_root
        self.pak_paths: dict[str, Path] = {}
        self._lock = threading.Lock()
        self._pak_stamps: dict[str, tuple[int, int]] = {}
        self._assets: dict[str, dict[str, bytes]] = {}
        self._cache: dict[tuple[str, str], bytes] = {}

    def validate(self) -> None:
        with self._lock:
            self._refresh_pak_paths()
            if not self.pak_paths:
                raise AssetPakError(f"No character packages found in: {self.pak_root}")
            for character in self.pak_paths:
                self._reload_if_needed(character)
                missing = [
                    name for name in ASSET_NAMES if name not in self._assets[character]
                ]
                if missing:
                    raise AssetPakError(
                        f"Missing {character} assets: {', '.join(missing)}"
                    )

    def character_ids(self) -> tuple[str, ...]:
        with self._lock:
            self._refresh_pak_paths()
            return tuple(self.pak_paths)

    def package_names(self) -> tuple[str, ...]:
        with self._lock:
            self._refresh_pak_paths()
            return tuple(path.name for path in self.pak_paths.values())

    def _refresh_pak_paths(self) -> None:
        discovered: dict[str, Path] = {}
        if self.pak_root.is_dir():
            for pak_path in sorted(
                self.pak_root.iterdir(), key=lambda path: path.name.casefold()
            ):
                if pak_path.is_file() and pak_path.suffix.casefold() == ".pak":
                    character = pak_path.stem.casefold()
                    if character:
                        discovered.setdefault(character, pak_path)

        removed = set(self.pak_paths) - set(discovered)
        if removed:
            self._assets = {
                key: value for key, value in self._assets.items() if key not in removed
            }
            self._pak_stamps = {
                key: value for key, value in self._pak_stamps.items() if key not in removed
            }
            self._cache = {
                key: value for key, value in self._cache.items() if key[0] not in removed
            }
        self.pak_paths = discovered

    def _reload_if_needed(self, character: str) -> None:
        pak_path = self.pak_paths[character]
        stat = pak_path.stat()
        stamp = (stat.st_mtime_ns, stat.st_size)
        if stamp == self._pak_stamps.get(character):
            return
        self._assets[character] = load_asset_pak(pak_path.read_bytes())
        self._cache = {
            key: value for key, value in self._cache.items() if key[0] != character
        }
        self._pak_stamps[character] = stamp

    def get(self, character: str, asset_name: str) -> bytes | None:
        if asset_name not in ASSET_ROUTES.values():
            return None

        with self._lock:
            self._refresh_pak_paths()
            if character not in self.pak_paths:
                return None
            try:
                self._reload_if_needed(character)
                cache_key = (character, asset_name)
                if cache_key in self._cache:
                    return self._cache[cache_key]
                source_bytes = self._assets[character][asset_name]
                if Image is None or ImageChops is None:
                    return source_bytes

                with Image.open(BytesIO(source_bytes)) as source:
                    rgba = source.convert("RGBA")

                red, green, blue, alpha = rgba.split()
                low_table = [
                    255 if value <= CHROMA_KEY_TOLERANCE else 0
                    for value in range(256)
                ]
                high_table = [
                    255 if value >= 255 - CHROMA_KEY_TOLERANCE else 0
                    for value in range(256)
                ]
                key_mask = ImageChops.multiply(red.point(low_table), green.point(high_table))
                key_mask = ImageChops.multiply(key_mask, blue.point(low_table))
                rgba.putalpha(ImageChops.subtract(alpha, key_mask))

                output = BytesIO()
                rgba.save(output, format="PNG")
                payload = output.getvalue()
            except (KeyError, OSError, ValueError, AssetPakError):
                return None

            self._cache[cache_key] = payload
            return payload


class ExclusiveThreadingHTTPServer(ThreadingHTTPServer):
    allow_reuse_address = False

    def server_bind(self) -> None:
        if os.name == "nt":
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        super().server_bind()


class OverlayHandler(SimpleHTTPRequestHandler):
    input_reader: WindowsInputReader
    chroma_assets: ChromaAssetStore

    def do_GET(self) -> None:
        path = urlparse(self.path).path
        if path == "/input-state":
            payload = json.dumps(self.input_reader.state(), ensure_ascii=False).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(payload)
            return

        if path.startswith("/media/"):
            route_parts = path.removeprefix("/media/").split("/", 1)
            if len(route_parts) == 2:
                character, route_name = route_parts
            else:
                character, route_name = DEFAULT_CHARACTER, route_parts[0]
            asset_name = ASSET_ROUTES.get(route_name)
            payload = (
                self.chroma_assets.get(unquote(character).casefold(), asset_name)
                if asset_name
                else None
            )
            if payload is not None:
                self.send_response(200)
                self.send_header("Content-Type", "image/png")
                self.send_header("Content-Length", str(len(payload)))
                self.send_header("X-Asset-Package", "encrypted-pak")
                self.send_header("X-Character", unquote(character).casefold())
                self.send_header("X-Chroma-Key", "00FF00")
                self.end_headers()
                self.wfile.write(payload)
                return
            self.send_error(404, "Packaged asset is unavailable")
            return

        if path.startswith("/assets/") or path.casefold().endswith(".pak"):
            self.send_error(404)
            return

        super().do_GET()

    def end_headers(self) -> None:
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, format_string: str, *args: object) -> None:
        path = urlparse(self.path).path
        if path != "/input-state" and not path.startswith("/media/"):
            super().log_message(format_string, *args)


def main() -> None:
    parser = argparse.ArgumentParser(description="OBS input relay for the pseudo-3D lever overlay")
    parser.add_argument("--port", type=int, default=8895)
    args = parser.parse_args()

    asset_store = ChromaAssetStore(CHARACTER_PAK_ROOT)
    try:
        asset_store.validate()
    except (OSError, AssetPakError) as error:
        raise SystemExit(f"Could not open encrypted character assets: {error}") from error
    binding_store = IniBindingStore(CONFIG_PATH, asset_store.character_ids)
    input_reader = WindowsInputReader(binding_store)
    OverlayHandler.input_reader = input_reader
    OverlayHandler.chroma_assets = asset_store
    handler = partial(OverlayHandler, directory=str(WEB_ROOT))

    try:
        server = ExclusiveThreadingHTTPServer(("127.0.0.1", args.port), handler)
    except OSError as error:
        raise SystemExit(
            f"Could not start localhost:{args.port}. "
            "Close any older copy of the display server and try again. "
            f"({error})"
        ) from error

    print("OBS input relay is running.")
    print(f"Browser Source URL: http://127.0.0.1:{args.port}/")
    print("Input settings:     InputSettings.exe")
    print(f"Character assets:   {' / '.join(asset_store.package_names())} loaded")
    print("Chroma key:         #00FF00 -> transparent")
    print("Keep this window open. Press Ctrl+C to stop.")

    try:
        server.serve_forever(poll_interval=0.25)
    except KeyboardInterrupt:
        print("\nStopping OBS input relay.")
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
