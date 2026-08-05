#!/usr/bin/env python3
"""Validates manifest.json.

Checks structure, the plugin GUID, and — critically — downloads the most recent
release asset and confirms its MD5 matches the declared checksum, so a wrong or
stale checksum (which would make Jellyfin refuse the install) fails instead of
shipping silently. Older versions are only HEAD-checked for reachability.

Shared by `.github/workflows/validate-manifest.yml` (PR + nightly) and
`release.yml` (before the manifest commit), so the published manifest is always
verified — including the `[skip ci]` release commit that PR checks never see.

Exits non-zero on any failure.
"""

import hashlib
import json
import pathlib
import sys
import urllib.request

PLUGIN_GUID = "f3b4d2c1-7e9a-4b1e-9c6d-9a1b2c3d4e5f"


def main() -> int:
    path = pathlib.Path("manifest.json")
    data = json.loads(path.read_text())
    assert isinstance(data, list) and data, "manifest must be a non-empty list"

    plugin = data[0]
    required = {"guid", "name", "owner", "category", "versions"}
    missing = required - plugin.keys()
    assert not missing, f"missing keys: {sorted(missing)}"
    assert plugin["guid"] == PLUGIN_GUID, "GUID drift"

    versions = plugin["versions"]
    assert versions, "no versions in manifest"
    for entry in versions:
        for key in ("version", "changelog", "targetAbi", "sourceUrl", "checksum", "timestamp"):
            assert key in entry, f"version missing key {key}"

    # Fully verify the most recent version: download the asset and confirm its MD5
    # matches the declared checksum.
    latest = versions[0]
    url = latest["sourceUrl"]
    print(f"downloading {url}")
    with urllib.request.urlopen(url, timeout=120) as resp:
        assert resp.status == 200, f"asset not reachable: {url}"
        content = resp.read()
    got = hashlib.md5(content).hexdigest()
    assert got == latest["checksum"], (
        f"checksum mismatch: declared {latest['checksum']} got {got}"
    )
    print(f"checksum OK for {latest['version']} ({got})")

    # Older versions: only confirm reachability (avoid re-downloading everything).
    for entry in versions[1:]:
        u = entry["sourceUrl"]
        print(f"checking {u}")
        req = urllib.request.Request(u, method="HEAD")
        with urllib.request.urlopen(req, timeout=20) as resp:
            assert resp.status == 200, f"asset not reachable: {u}"

    print("manifest OK")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # noqa: BLE001 - surface any validation failure
        print(f"manifest validation FAILED: {exc}", file=sys.stderr)
        sys.exit(1)
