from __future__ import annotations

import argparse
import json
import sys
from array import array
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


WIDTH = 800
HEIGHT = 480


def _rgb565(red: int, green: int, blue: int) -> int:
    return (red & 0xF8) << 8 | (green & 0xFC) << 3 | blue >> 3


WHITE = 0xFFFF
CYAN = _rgb565(76, 220, 255)
PURPLE = _rgb565(136, 119, 255)
GREEN = _rgb565(68, 214, 165)
LABEL = _rgb565(190, 204, 219)
TRACK = _rgb565(41, 52, 66)


@dataclass(frozen=True)
class BitmapFont:
    pixel_size: int
    bitmap: bytes
    glyphs: dict[int, dict[str, int]]


def _asset_dir(root: Path) -> Path:
    return root / "firmware" / "components" / "ui_assets"


def _load_surface(path: Path) -> list[int]:
    raw = path.read_bytes()
    if len(raw) != WIDTH * HEIGHT * 2:
        raise ValueError(f"expected {WIDTH}x{HEIGHT} RGB565 data: {path}")
    values = array("H")
    values.frombytes(raw)
    if sys.byteorder != "little":
        values.byteswap()
    return list(values)


def _load_font(assets: Path, pixel_size: int) -> BitmapFont:
    metadata = json.loads((assets / "generated_font_metadata.json").read_text(encoding="utf-8"))
    glyphs = {record["codepoint"]: record for record in metadata[str(pixel_size)]}
    return BitmapFont(
        pixel_size=pixel_size,
        bitmap=(assets / f"generated_font_{pixel_size}.bin").read_bytes(),
        glyphs=glyphs,
    )


def _blend565(destination: int, source: int, alpha: int) -> int:
    source_red = (source >> 11) & 31
    source_green = (source >> 5) & 63
    source_blue = source & 31
    destination_red = (destination >> 11) & 31
    destination_green = (destination >> 5) & 63
    destination_blue = destination & 31
    inverse = 255 - alpha
    red = (source_red * alpha + destination_red * inverse + 127) // 255
    green = (source_green * alpha + destination_green * inverse + 127) // 255
    blue = (source_blue * alpha + destination_blue * inverse + 127) // 255
    return red << 11 | green << 5 | blue


def _fill_rect(surface: list[int], rect: tuple[int, int, int, int], color: int) -> None:
    x, y, width, height = rect
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(WIDTH, x + width), min(HEIGHT, y + height)
    for row in range(y0, y1):
        start = row * WIDTH + x0
        surface[start:start + x1 - x0] = [color] * (x1 - x0)


def _draw_progress(
    surface: list[int], rect: tuple[int, int, int, int], percent: int, color: int
) -> None:
    _fill_rect(surface, rect, TRACK)
    x, y, width, height = rect
    _fill_rect(surface, (x, y, width * min(max(percent, 0), 100) // 100, height), color)


def _draw_text(
    surface: list[int], x: int, baseline: int, text: str, font: BitmapFont, color: int
) -> int:
    pen = x
    for character in text:
        glyph = font.glyphs.get(ord(character))
        if glyph is None:
            pen += font.pixel_size // 2
            continue
        for glyph_y in range(glyph["height"]):
            destination_y = baseline + glyph["y_offset"] + glyph_y
            if destination_y < 0 or destination_y >= HEIGHT:
                continue
            for glyph_x in range(glyph["width"]):
                destination_x = pen + glyph["x_offset"] + glyph_x
                if destination_x < 0 or destination_x >= WIDTH:
                    continue
                alpha = font.bitmap[
                    glyph["offset"] + glyph_y * glyph["width"] + glyph_x
                ]
                index = destination_y * WIDTH + destination_x
                surface[index] = _blend565(surface[index], color, alpha)
        pen += glyph["advance"]
    return pen


def _draw_rgba565(
    surface: list[int], path: Path, x: int, y: int, width: int, height: int
) -> None:
    pixels = path.read_bytes()
    if len(pixels) != width * height * 3:
        raise ValueError(f"unexpected RGBA565 size: {path}")
    for image_y in range(height):
        for image_x in range(width):
            offset = (image_y * width + image_x) * 3
            color = pixels[offset] | pixels[offset + 1] << 8
            alpha = pixels[offset + 2]
            if alpha:
                index = (y + image_y) * WIDTH + x + image_x
                surface[index] = _blend565(surface[index], color, alpha)


def _surface_to_image(surface: list[int]) -> Image.Image:
    pixels = bytearray(WIDTH * HEIGHT * 3)
    for index, value in enumerate(surface):
        offset = index * 3
        pixels[offset] = ((value >> 11) & 31) * 255 // 31
        pixels[offset + 1] = ((value >> 5) & 63) * 255 // 63
        pixels[offset + 2] = (value & 31) * 255 // 31
    return Image.frombytes("RGB", (WIDTH, HEIGHT), bytes(pixels))


def _render_pc(root: Path) -> Image.Image:
    assets = _asset_dir(root)
    surface = _load_surface(assets / "generated_page_pc.rgb565")
    font20 = _load_font(assets, 20)
    font24 = _load_font(assets, 24)
    font56 = _load_font(assets, 56)

    _draw_text(surface, 56, 40, "86.4 Mbps", font20, CYAN)
    _draw_text(surface, 272, 40, "24.8 Mbps", font20, CYAN)
    _draw_rgba565(surface, assets / "generated_wifi_up.rgba565", 570, 18, 28, 28)
    _draw_text(surface, 610, 40, "Solis Wi-Fi", font20, GREEN)

    _draw_text(surface, 84, 108, "Intel Core i7-14700K", font20, WHITE)
    _draw_text(surface, 40, 174, "37%", font56, CYAN)
    _draw_text(surface, 190, 198, "5.2GHz  78W  58°C", font20, LABEL)

    _draw_text(surface, 460, 108, "NVIDIA RTX 4070", font20, WHITE)
    _draw_text(surface, 416, 174, "62%", font56, PURPLE)
    _draw_text(surface, 550, 172, "2.5GHz  142W  61°C", font20, LABEL)
    _draw_text(surface, 550, 212, "8.2G/12.0G  64°C", font20, WHITE)

    _draw_text(surface, 84, 302, "内存", font20, LABEL)
    _draw_text(surface, 40, 360, "18.6 / 32.0 GB", font24, WHITE)
    _draw_text(surface, 40, 414, "58%   44°C", font20, CYAN)

    _draw_text(surface, 342, 302, "物理硬盘", font20, LABEL)
    _draw_text(surface, 304, 342, "System NVMe", font20, WHITE)
    _draw_text(surface, 650, 342, "42%  39°C", font20, CYAN)
    _draw_text(surface, 304, 370, "Data NVMe", font20, WHITE)
    _draw_text(surface, 650, 370, "67%  43°C", font20, CYAN)

    return _surface_to_image(surface)


def _render_codex(root: Path) -> Image.Image:
    assets = _asset_dir(root)
    surface = _load_surface(assets / "generated_page_codex.rgb565")
    font20 = _load_font(assets, 20)
    font24 = _load_font(assets, 24)

    _draw_text(surface, 54, 40, "24.6°C", font20, CYAN)
    _draw_text(surface, 250, 40, "48%", font20, GREEN)
    _draw_text(surface, 620, 40, "CODEX 活跃", font20, GREEN)

    _draw_text(surface, 40, 106, "上下文", font20, LABEL)
    _draw_text(surface, 40, 142, "项目  Solis_Monitor", font20, WHITE)
    _draw_text(surface, 40, 176, "模型 GPT-5   推理 high", font20, LABEL)
    _draw_text(surface, 40, 218, "96.0K / 256.0K", font24, WHITE)
    _draw_text(surface, 276, 218, "38%", font24, CYAN)
    _draw_progress(surface, (40, 240, 364, 12), 38, CYAN)

    _draw_text(surface, 448, 106, "主周额度", font20, LABEL)
    _draw_text(surface, 700, 106, "72%", font24, WHITE)
    _draw_progress(surface, (448, 128, 312, 8), 72, PURPLE)
    _draw_text(surface, 448, 160, "重置 周一 08:00", font20, LABEL)
    _fill_rect(surface, (448, 188, 312, 2), TRACK)
    _draw_text(surface, 448, 218, "GPT-5.3-Codex-Spark", font20, LABEL)
    _draw_text(surface, 700, 218, "86%", font24, WHITE)
    _draw_progress(surface, (448, 240, 312, 8), 86, PURPLE)
    _draw_text(surface, 448, 272, "重置 周一 08:00", font20, LABEL)

    _draw_rgba565(surface, assets / "generated_weather_m00.rgba565", 40, 378, 48, 48)
    _draw_text(surface, 80, 370, "天气位置", font20, LABEL)
    _draw_text(surface, 100, 402, "晴  19°C/27°C", font24, WHITE)
    _draw_text(surface, 100, 436, "东北风  3级", font20, LABEL)
    _draw_text(surface, 410, 374, "周使用 TOKEN", font20, LABEL)
    _draw_text(surface, 410, 424, "0.18亿", font24, GREEN)
    _draw_text(surface, 590, 374, "账户累计 TOKEN", font20, LABEL)
    _draw_text(surface, 590, 424, "1.26亿", font24, CYAN)

    return _surface_to_image(surface)


def _create_hero(root: Path, screen: Image.Image) -> Image.Image:
    canvas = Image.new("RGB", (1400, 900), (6, 11, 18))
    background = ImageDraw.Draw(canvas)
    for y in range(900):
        blue = 18 + y * 12 // 900
        background.line((0, y, 1400, y), fill=(6, 11 + y * 5 // 900, blue))

    shadow_layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    shadow = ImageDraw.Draw(shadow_layer)
    shadow.rounded_rectangle((106, 116, 1294, 824), radius=46, fill=(0, 0, 0, 180))
    shadow_layer = shadow_layer.filter(ImageFilter.GaussianBlur(28))
    canvas = Image.alpha_composite(canvas.convert("RGBA"), shadow_layer)

    body_layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    body = ImageDraw.Draw(body_layer)
    body.rounded_rectangle(
        (120, 92, 1280, 796),
        radius=42,
        fill=(20, 24, 30, 255),
        outline=(71, 79, 90, 255),
        width=3,
    )
    body.rounded_rectangle((150, 120, 1250, 780), radius=24, fill=(2, 5, 8, 255))
    canvas = Image.alpha_composite(canvas, body_layer)

    fitted_screen = screen.resize((1060, 636), Image.Resampling.LANCZOS)
    canvas.alpha_composite(fitted_screen.convert("RGBA"), dest=(170, 132))

    glass = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    glass_draw = ImageDraw.Draw(glass)
    glass_draw.polygon(
        ((170, 132), (760, 132), (450, 768), (170, 768)),
        fill=(255, 255, 255, 10),
    )
    glass_draw.ellipse((1242, 761, 1252, 771), fill=(68, 214, 165, 255))
    font_path = root / "reference" / "assets" / "HarmonyOS_Sans_SC_Medium.ttf"
    label_font = ImageFont.truetype(font_path, 18)
    glass_draw.text((700, 775), "SOLIS MONITOR", font=label_font, fill=(128, 139, 153, 255), anchor="mm")
    canvas = Image.alpha_composite(canvas, glass)
    return canvas.convert("RGB")


def render_all(root: Path, output: Path) -> tuple[Path, Path, Path]:
    root = root.resolve()
    output.mkdir(parents=True, exist_ok=True)
    pc = _render_pc(root)
    codex = _render_codex(root)
    hero = _create_hero(root, pc)
    paths = (
        output / "small-screen-pc.png",
        output / "small-screen-codex.png",
        output / "small-screen-hero.png",
    )
    for path, image in zip(paths, (pc, codex, hero), strict=True):
        image.save(path, format="PNG", optimize=False)
    return paths


def main() -> None:
    parser = argparse.ArgumentParser(description="Render Solis Monitor UI previews")
    parser.add_argument(
        "--root", type=Path, default=Path(__file__).resolve().parents[1]
    )
    parser.add_argument(
        "--output", type=Path, default=Path("docs/images")
    )
    arguments = parser.parse_args()
    for path in render_all(arguments.root, arguments.output):
        print(path)


if __name__ == "__main__":
    main()
