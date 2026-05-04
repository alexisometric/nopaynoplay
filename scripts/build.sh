#!/usr/bin/env bash
# Compile et empaquette le plugin NoPayNoPlay au format Jellyfin.
# Sortie : artifacts/nopaynoplay_<version>.zip + checksum
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

VERSION="${1:-$(grep -m1 '<Version>' src/Jellyfin.Plugin.NoPayNoPlay.csproj | sed -E 's@.*<Version>([^<]+)</Version>.*@\1@')}"
TARGET_ABI="${TARGET_ABI:-10.11.8.0}"
OUT_DIR="$ROOT/artifacts"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

echo ">> Build $VERSION (ABI $TARGET_ABI)"

dotnet restore src/Jellyfin.Plugin.NoPayNoPlay.csproj
dotnet build src/Jellyfin.Plugin.NoPayNoPlay.csproj \
    -c Release \
    -p:Version="$VERSION" \
    -p:AssemblyVersion="$VERSION" \
    -p:FileVersion="$VERSION"

mkdir -p "$OUT_DIR"

# Mise en forme du meta.json avec la version cible.
python3 - "$VERSION" "$TARGET_ABI" <<'PY' > "$WORK_DIR/meta.json"
import json, sys, pathlib
version, abi = sys.argv[1], sys.argv[2]
data = json.loads(pathlib.Path("src/meta.json").read_text())
data["version"] = version
data["targetAbi"] = abi
print(json.dumps(data, indent=2, ensure_ascii=False))
PY

cp "src/bin/Release/net9.0/Jellyfin.Plugin.NoPayNoPlay.dll" "$WORK_DIR/"

ZIP="$OUT_DIR/nopaynoplay_${VERSION}.zip"
rm -f "$ZIP"
( cd "$WORK_DIR" && zip -q -9 "$ZIP" Jellyfin.Plugin.NoPayNoPlay.dll meta.json )

# Checksum md5 attendu par Jellyfin dans le manifest.
MD5="$(md5sum "$ZIP" | awk '{print $1}')"
echo "$MD5  $(basename "$ZIP")" > "$ZIP.md5"

echo ">> $ZIP"
echo ">> md5 $MD5"
