#!/usr/bin/env bash
# Updates manifest.json at the repo root by adding the current version entry.
# Usage: ./scripts/update-manifest.sh <version> <zip-url> <md5>
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

VERSION="$1"
ZIP_URL="$2"
MD5="$3"
# Default the ABI to src/meta.json (single source of truth); fall back only if unreadable.
TARGET_ABI="${TARGET_ABI:-$(python3 -c 'import json;print(json.load(open("src/meta.json"))["targetAbi"])' 2>/dev/null || true)}"
[[ -z "${TARGET_ABI:-}" || "$TARGET_ABI" == "null" ]] && TARGET_ABI="10.11.9.0"
CHANGELOG="${CHANGELOG:-See the GitHub release notes.}"

python3 - "$VERSION" "$ZIP_URL" "$MD5" "$TARGET_ABI" "$CHANGELOG" <<'PY'
import json, sys, pathlib, datetime
version, url, md5, abi, changelog = sys.argv[1:6]
path = pathlib.Path("manifest.json")
manifest = json.loads(path.read_text()) if path.exists() else []

# Repository skeleton (single plugin for now).
plugin = next((p for p in manifest if p.get("guid") == "f3b4d2c1-7e9a-4b1e-9c6d-9a1b2c3d4e5f"), None)
if plugin is None:
    plugin = {
        "guid": "f3b4d2c1-7e9a-4b1e-9c6d-9a1b2c3d4e5f",
        "name": "NoPayNoPlay",
        "description": "Manual-validation monthly subscription tracking for Jellyfin with playback enforcement on expiry, an admin page and an in-app user UI.",
        "overview": "Tracks a monthly subscription per Jellyfin user. Admins validate payments manually; playback is blocked when the subscription expires without deleting the account.",
        "owner": "alexisometric",
        "category": "General",
        "imageUrl": "https://raw.githubusercontent.com/alexisometric/nopaynoplay/main/images/logo.png",
        "versions": []
    }
    manifest.append(plugin)

# Avoid duplicates (same version) -> replace.
plugin["versions"] = [v for v in plugin["versions"] if v.get("version") != version]
plugin["versions"].insert(0, {
    "version": version,
    "changelog": changelog,
    "targetAbi": abi,
    "sourceUrl": url,
    "checksum": md5,
    "timestamp": datetime.datetime.now(datetime.UTC).strftime("%Y-%m-%dT%H:%M:%SZ")
})

path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")
print(f"manifest.json updated for {version}")
PY
