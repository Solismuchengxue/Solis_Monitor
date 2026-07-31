import gzip
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "firmware" / "components" / "network_client" / "portal.html"
ARCHIVE = SOURCE.with_suffix(".html.gz")


class ProvisioningPortalAssetsTests(unittest.TestCase):
    def test_committed_gzip_matches_portal_source(self):
        self.assertEqual(SOURCE.read_bytes(), gzip.decompress(ARCHIVE.read_bytes()))

    def test_portal_contains_required_fields_and_endpoints(self):
        html = SOURCE.read_text(encoding="utf-8")
        for value in ("ssid", "password"):
            self.assertIn(f'id="{value}"', html)
        for value in ("host", "port", "token", "auth"):
            self.assertNotIn(f'id="{value}"', html)
        self.assertNotIn("Windows IPv4", html)
        self.assertNotIn("64 位设备令牌", html)
        self.assertNotIn("Device API 端口", html)
        self.assertIn("/api/scan", html)
        self.assertIn("/api/config", html)
        self.assertIn("/api/reset", html)
        self.assertIn('type="password"', html)
        self.assertIn("恢复默认设置", html)
        self.assertIn("confirm(", html)
        self.assertNotIn("\\\\n", html)
        self.assertNotIn("可直接编辑", html)
        self.assertNotIn("双击 GPIO21", html)

    def test_generator_is_deterministic(self):
        before = ARCHIVE.read_bytes()
        subprocess.run(["python", str(ROOT / "tools" / "generate_portal.py")],
                       cwd=ROOT, check=True)
        self.assertEqual(before, ARCHIVE.read_bytes())


if __name__ == "__main__":
    unittest.main()
