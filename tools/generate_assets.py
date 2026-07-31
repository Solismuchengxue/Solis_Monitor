from __future__ import annotations

import argparse
import json
import struct
from io import BytesIO
from pathlib import Path

from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

W, H = 800, 480
FONT_SIZES = (20, 24, 56)
PC_CARDS = ((24, 68, 388, 250), (400, 68, 776, 250),
            (24, 262, 270, 456), (282, 262, 776, 456))
CODEX_CARDS = ((24, 68, 420, 320), (432, 68, 776, 320),
               (24, 332, 776, 456))
PC_ICONS = (("CPU.png", (40, 82), (36, 36)),
            ("GPU.png", (416, 82), (36, 36)),
            ("RAM.png", (40, 276), (36, 36)),
            ("disk.png", (298, 276), (36, 36)),
            ("download.png", (24, 20), (24, 24)),
            ("upload.png", (240, 20), (24, 24)))
CODEX_ICONS = (("temper.png", (24, 18), (24, 24)),
               ("humid.png", (220, 18), (24, 24)),
               ("location.png", (40, 348), (28, 28)))
WEATHER_ICON_SIZE = (48, 48)
WEATHER_ICON_COUNT = 27
STATUS_ICON_SIZE = (28, 28)


def _assets_dir(root: Path) -> Path:
    # Keep backward compatibility while preferring the refactored repo layout.
    direct = root / "assets"
    nested = root / "reference" / "assets"
    if nested.exists():
        return nested
    return direct


def _flattened_pixels(image: Image.Image):
    get_flattened_data = getattr(image, "get_flattened_data", None)
    if get_flattened_data is not None:
        return get_flattened_data()
    return image.getdata()


def rgb565_le(image: Image.Image) -> bytes:
    pixels = bytearray()
    for r, g, b in _flattened_pixels(image.convert("RGB")):
        value = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)
        pixels.extend(struct.pack("<H", value))
    return bytes(pixels)


def rgba565_le(image: Image.Image) -> bytes:
    pixels = bytearray()
    for r, g, b, a in _flattened_pixels(image.convert("RGBA")):
        value = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)
        pixels.extend(struct.pack("<HB", value, a))
    return bytes(pixels)


def _base(root: Path) -> Image.Image:
    image = Image.open(_assets_dir(root) / "background.png").convert("RGBA")
    if image.size != (W, H):
        raise ValueError(f"background must be {W}x{H}, got {image.size}")
    overlay = Image.new("RGBA", image.size, (3, 13, 20, 198))
    return Image.alpha_composite(image, overlay)


def _card(draw: ImageDraw.ImageDraw, rect: tuple[int, int, int, int], codex=False):
    fill = (13, 15, 35, 220) if codex else (5, 26, 38, 214)
    outline = (157, 126, 255, 160) if codex else (85, 213, 255, 110)
    draw.rounded_rectangle(rect, radius=10, fill=fill, outline=outline, width=1)


def _paste_icons(image: Image.Image, root: Path, icons) -> None:
    for name, xy, size in icons:
        icon = Image.open(_assets_dir(root) / name).convert("RGBA")
        icon = icon.resize(size, Image.Resampling.LANCZOS)
        image.alpha_composite(icon, dest=xy)


def build_background(root: Path, page: str) -> Image.Image:
    image = _base(root)
    draw = ImageDraw.Draw(image, "RGBA")
    if page == "pc":
        for rect in PC_CARDS:
            _card(draw, rect)
        _paste_icons(image, root, PC_ICONS)
    elif page == "codex":
        for rect in CODEX_CARDS:
            _card(draw, rect, codex=True)
        _paste_icons(image, root, CODEX_ICONS)
    else:
        raise ValueError(page)
    return image.convert("RGB")


def build_fonts(root: Path, out: Path) -> None:
    font_path = _assets_dir(root) / "HarmonyOS_Sans_SC_Medium.ttf"
    glyph_text = (root / "tools" / "glyphs.txt").read_text(encoding="utf-8").rstrip("\n")
    codepoints = sorted(set(map(ord, glyph_text)))
    with TTFont(font_path, lazy=True) as ttfont:
        cmap = ttfont.getBestCmap()
    missing = [chr(cp) for cp in codepoints if cp not in cmap]
    if missing:
        raise ValueError("font missing glyphs: " + "".join(missing))

    metadata = {}
    for size in FONT_SIZES:
        font = ImageFont.truetype(BytesIO(font_path.read_bytes()), size=size)
        pixels = bytearray()
        records = []
        for cp in codepoints:
            ch = chr(cp)
            left, top, right, bottom = font.getbbox(ch, anchor="ls")
            width, height = max(0, right - left), max(0, bottom - top)
            offset = len(pixels)
            if width and height:
                mask = Image.new("L", (width, height), 0)
                ImageDraw.Draw(mask).text((-left, -top), ch, font=font,
                                          fill=255, anchor="ls")
                pixels.extend(mask.tobytes())
            records.append({"codepoint": cp, "offset": offset, "width": width,
                            "height": height, "x_offset": left, "y_offset": top,
                            "advance": round(font.getlength(ch))})
        (out / f"generated_font_{size}.bin").write_bytes(pixels)
        metadata[str(size)] = records
    (out / "generated_font_metadata.json").write_text(
        json.dumps(metadata, ensure_ascii=False, sort_keys=True, separators=(",", ":")),
        encoding="utf-8",
    )
    emit_c_metadata(out, metadata)


def emit_c_metadata(out: Path, metadata: dict[str, list[dict[str, int]]]) -> None:
    lines = ['#include "renderer.h"', '']
    for size in FONT_SIZES:
        lines.append(
            f'extern const uint8_t font{size}_start[] '
            f'asm("_binary_generated_font_{size}_bin_start");'
        )
    lines.append('')
    for size in FONT_SIZES:
        lines.append(f'static const bitmap_glyph_t glyphs_{size}[] = {{')
        for record in metadata[str(size)]:
            lines.append(
                '    {' + ', '.join(str(record[key]) for key in (
                    'codepoint', 'offset', 'width', 'height',
                    'x_offset', 'y_offset', 'advance')) + '},'
            )
        lines.extend([
            '};',
            f'const bitmap_font_t generated_font_{size} = {{',
            f'    .bitmap = font{size}_start,',
            f'    .glyphs = glyphs_{size},',
            f'    .glyph_count = sizeof(glyphs_{size}) / sizeof(glyphs_{size}[0]),',
            f'    .pixel_size = {size},',
            '};',
            '',
        ])
    (out / 'generated_font_metadata.c').write_text(
        '\n'.join(lines), encoding='utf-8', newline='\n'
    )


def build_all(root: Path, out: Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    for page in ("pc", "codex"):
        data = rgb565_le(build_background(root, page))
        (out / f"generated_page_{page}.rgb565").write_bytes(data)
    for index in range(WEATHER_ICON_COUNT):
        icon = Image.open(_assets_dir(root) / f"m{index:02d}.png").convert("RGBA")
        icon = icon.resize(WEATHER_ICON_SIZE, Image.Resampling.LANCZOS)
        (out / f"generated_weather_m{index:02d}.rgba565").write_bytes(
            rgba565_le(icon)
        )
    for name in ("wifi_up", "wifi_down"):
        icon = Image.open(_assets_dir(root) / f"{name}.png").convert("RGBA")
        icon = icon.resize(STATUS_ICON_SIZE, Image.Resampling.LANCZOS)
        (out / f"generated_{name}.rgba565").write_bytes(rgba565_le(icon))
    build_fonts(root, out)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, default=Path("firmware/components/ui_assets"))
    args = parser.parse_args()
    build_all(args.root.resolve(), args.output.resolve())
