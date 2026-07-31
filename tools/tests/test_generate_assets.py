import hashlib
import tempfile
import unittest
import warnings
from pathlib import Path

from PIL import Image

from tools.generate_assets import (
    CODEX_CARDS,
    CODEX_ICONS,
    STATUS_ICON_SIZE,
    WEATHER_ICON_COUNT,
    WEATHER_ICON_SIZE,
    build_all,
    rgb565_le,
    rgba565_le,
)


class AssetGenerationTests(unittest.TestCase):
    def test_runtime_ui_text_is_in_font_glyph_set(self):
        root = Path(__file__).resolve().parents[2]
        glyphs = set((root / "tools" / "glyphs.txt").read_text(encoding="utf-8"))
        runtime_text = (
            "开启发现请在PC端设备向导中选择此设备"
            "配对码秒后刷新"
            "已成功配对PC已获得设备访问权限单击退出"
            "辽宁·大连"
        )

        self.assertEqual(set(runtime_text) - glyphs, set())

    def test_codex_layout_has_environment_icons_and_lowered_weather_card(self):
        icon_names = {name for name, _, _ in CODEX_ICONS}

        self.assertIn("temper.png", icon_names)
        self.assertIn("humid.png", icon_names)
        self.assertIn("location.png", icon_names)
        self.assertEqual(CODEX_CARDS[0][3], 320)
        self.assertEqual(CODEX_CARDS[1][3], 320)
        self.assertEqual(CODEX_CARDS[2][1], 332)

    def test_rgb565_is_little_endian_native_word_data(self):
        image = Image.new("RGB", (3, 1))
        image.putdata([(255, 0, 0), (0, 255, 0), (0, 0, 255)])
        self.assertEqual(rgb565_le(image), b"\x00\xf8\xe0\x07\x1f\x00")

    def test_rgba565_keeps_per_pixel_alpha(self):
        image = Image.new("RGBA", (2, 1))
        image.putdata([(255, 0, 0, 255), (0, 255, 0, 64)])
        self.assertEqual(rgba565_le(image), b"\x00\xf8\xff\xe0\x07\x40")

    def test_pixel_encoders_do_not_use_deprecated_pillow_api(self):
        rgb = Image.new("RGB", (1, 1), (1, 2, 3))
        rgba = Image.new("RGBA", (1, 1), (1, 2, 3, 4))

        with warnings.catch_warnings():
            warnings.simplefilter("error", DeprecationWarning)
            rgb565_le(rgb)
            rgba565_le(rgba)

    def test_build_outputs_exact_screen_sizes_and_is_reproducible(self):
        root = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory() as a, tempfile.TemporaryDirectory() as b:
            out_a, out_b = Path(a), Path(b)
            build_all(root, out_a)
            build_all(root, out_b)
            for name in ("generated_page_pc.rgb565", "generated_page_codex.rgb565"):
                self.assertEqual((out_a / name).stat().st_size, 800 * 480 * 2)
            self.assertEqual(WEATHER_ICON_COUNT, 27)
            for index in range(WEATHER_ICON_COUNT):
                name = f"generated_weather_m{index:02d}.rgba565"
                self.assertEqual(
                    (out_a / name).stat().st_size,
                    WEATHER_ICON_SIZE[0] * WEATHER_ICON_SIZE[1] * 3,
                )
            for name in ("generated_wifi_up.rgba565", "generated_wifi_down.rgba565"):
                self.assertEqual(
                    (out_a / name).stat().st_size,
                    STATUS_ICON_SIZE[0] * STATUS_ICON_SIZE[1] * 3,
                )
            files_a = sorted(p.name for p in out_a.iterdir())
            files_b = sorted(p.name for p in out_b.iterdir())
            self.assertEqual(files_a, files_b)
            for name in files_a:
                self.assertEqual(
                    hashlib.sha256((out_a / name).read_bytes()).digest(),
                    hashlib.sha256((out_b / name).read_bytes()).digest(),
                )


if __name__ == "__main__":
    unittest.main()
