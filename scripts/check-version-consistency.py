#!/usr/bin/env python3
"""Check version consistency across csproj, meta.json and manifest.json.

Default mode:
  - the three <Version>/<FileVersion>/<AssemblyVersion> tags of the csproj must agree,
  - src/meta.json "version" must equal the csproj <Version>.

With --require-manifest (used post-release / on main, NOT on PRs where the manifest
legitimately lags a version bump):
  - additionally, manifest.json's latest published version must equal the csproj <Version>.

Exit code 0 on success, 1 on any inconsistency.
"""
import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSPROJ = ROOT / "src" / "Jellyfin.Plugin.NoPayNoPlay.csproj"
META = ROOT / "src" / "meta.json"
MANIFEST = ROOT / "manifest.json"
PLUGIN_GUID = "f3b4d2c1-7e9a-4b1e-9c6d-9a1b2c3d4e5f"


def csproj_versions() -> dict:
    text = CSPROJ.read_text(encoding="utf-8")
    out = {}
    for tag in ("Version", "FileVersion", "AssemblyVersion"):
        m = re.search(rf"<{tag}>(.*?)</{tag}>", text)
        out[tag] = m.group(1) if m else None
    return out


def meta_version() -> str:
    return json.loads(META.read_text(encoding="utf-8")).get("version")


def manifest_latest_version():
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    for entry in data:
        if entry.get("guid") == PLUGIN_GUID:
            versions = entry.get("versions") or []
            return versions[0]["version"] if versions else None
    return None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--require-manifest",
        action="store_true",
        help="also require manifest.json latest == csproj version (post-release check)",
    )
    args = ap.parse_args()

    cs = csproj_versions()
    errors = []

    if any(v is None for v in cs.values()):
        errors.append(f"csproj: missing one of {sorted(cs)} tags")
    else:
        for tag, v in cs.items():
            if v != cs["Version"]:
                errors.append(f"csproj: <{tag}>={v} != <Version>={cs['Version']}")

        meta = meta_version()
        if meta != cs["Version"]:
            errors.append(f"meta.json: version={meta} != csproj <Version>={cs['Version']}")

        if args.require_manifest:
            latest = manifest_latest_version()
            if latest != cs["Version"]:
                errors.append(
                    f"manifest.json: latest published={latest} != csproj <Version>={cs['Version']}"
                )

    if errors:
        print("Version consistency check FAILED:")
        for e in errors:
            print(f"  - {e}")
        return 1

    detail = f"csproj={cs['Version']}, meta={meta_version()}"
    if args.require_manifest:
        detail += f", manifest={manifest_latest_version()}"
    print(f"Version consistency OK ({detail})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
