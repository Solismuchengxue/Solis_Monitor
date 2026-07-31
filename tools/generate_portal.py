from __future__ import annotations

import gzip
from pathlib import Path


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    source = root / "firmware" / "components" / "network_client" / "portal.html"
    target = source.with_suffix(".html.gz")
    target.write_bytes(gzip.compress(source.read_bytes(), compresslevel=9, mtime=0))


if __name__ == "__main__":
    main()
