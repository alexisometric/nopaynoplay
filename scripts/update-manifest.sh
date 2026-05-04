#!/usr/bin/env bash
# Met à jour manifest.json à la racine en ajoutant l'entrée de version courante.
# Usage : ./scripts/update-manifest.sh <version> <zip-url> <md5>
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

VERSION="$1"
ZIP_URL="$2"
MD5="$3"
TARGET_ABI="${TARGET_ABI:-10.11.8.0}"
CHANGELOG="${CHANGELOG:-Voir les notes de release sur GitHub.}"

python3 - "$VERSION" "$ZIP_URL" "$MD5" "$TARGET_ABI" "$CHANGELOG" <<'PY'
import json, sys, pathlib, datetime
version, url, md5, abi, changelog = sys.argv[1:6]
path = pathlib.Path("manifest.json")
manifest = json.loads(path.read_text()) if path.exists() else []

# Squelette du dépôt (un seul plugin pour l'instant).
plugin = next((p for p in manifest if p.get("guid") == "f3b4d2c1-7e9a-4b1e-9c6d-9a1b2c3d4e5f"), None)
if plugin is None:
    plugin = {
        "guid": "f3b4d2c1-7e9a-4b1e-9c6d-9a1b2c3d4e5f",
        "name": "NoPayNoPlay",
        "description": "Suivi automatique des abonnements mensuels avec validation manuelle.",
        "overview": "Gestion d'un abonnement mensuel pour Jellyfin (10€/mois configurable).",
        "owner": "alexis",
        "category": "General",
        "imageUrl": "",
        "versions": []
    }
    manifest.append(plugin)

# Évite les doublons (même version) -> remplace.
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
print(f"manifest.json mis à jour pour {version}")
PY
