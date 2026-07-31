import hashlib
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from tools.render_ui_preview import render_all


class UiPreviewTests(unittest.TestCase):
    def test_render_all_creates_three_nonempty_rgb_previews(self):
        root = Path(__file__).resolve().parents[2]
        expected_sizes = {
            "small-screen-pc.png": (800, 480),
            "small-screen-codex.png": (800, 480),
            "small-screen-hero.png": (1400, 900),
        }

        with tempfile.TemporaryDirectory() as directory:
            outputs = render_all(root, Path(directory))

            self.assertEqual({path.name for path in outputs}, set(expected_sizes))
            for path in outputs:
                with Image.open(path) as image:
                    self.assertEqual(image.size, expected_sizes[path.name])
                    self.assertEqual(image.mode, "RGB")
                    self.assertIsNone(image.getcolors(maxcolors=256))

    def test_render_all_is_reproducible(self):
        root = Path(__file__).resolve().parents[2]

        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            first_outputs = render_all(root, Path(first))
            second_outputs = render_all(root, Path(second))

            first_hashes = {
                path.name: hashlib.sha256(path.read_bytes()).digest()
                for path in first_outputs
            }
            second_hashes = {
                path.name: hashlib.sha256(path.read_bytes()).digest()
                for path in second_outputs
            }
            self.assertEqual(first_hashes, second_hashes)

    def test_pc_preview_keeps_static_background_pixel_from_firmware_asset(self):
        root = Path(__file__).resolve().parents[2]
        raw = (root / "firmware/components/ui_assets/generated_page_pc.rgb565").read_bytes()
        value = raw[0] | raw[1] << 8
        expected_rgb = (
            ((value >> 11) & 31) * 255 // 31,
            ((value >> 5) & 63) * 255 // 63,
            (value & 31) * 255 // 31,
        )

        with tempfile.TemporaryDirectory() as directory:
            render_all(root, Path(directory))
            with Image.open(Path(directory) / "small-screen-pc.png") as image:
                self.assertEqual(image.getpixel((0, 0)), expected_rgb)


if __name__ == "__main__":
    unittest.main()
