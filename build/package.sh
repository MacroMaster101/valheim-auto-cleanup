#!/usr/bin/env bash
#
# Builds the plugin and assembles a Thunderstore-ready zip in dist/.
#
# The archive contains ONLY files this project owns:
#   ValheimAutoCleanup.dll  README.md  CHANGELOG.md  manifest.json  icon.png
#
# It must never contain assembly_valheim.dll, Unity assemblies, BepInEx binaries
# or any other Valheim game file. Those are copyrighted third-party binaries.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(grep -oE '"version_number"[[:space:]]*:[[:space:]]*"[^"]+"' "$ROOT/manifest.json" \
            | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')"
STAGE="$ROOT/dist/stage"
OUT="$ROOT/dist/ValheimAutoCleanup-$VERSION.zip"

echo "Building ValheimAutoCleanup $VERSION"

dotnet build "$ROOT/src/ValheimAutoCleanup/ValheimAutoCleanup.csproj" -c Release --nologo

DLL="$ROOT/src/ValheimAutoCleanup/bin/Release/ValheimAutoCleanup.dll"
[ -f "$DLL" ] || { echo "Build output not found at $DLL" >&2; exit 1; }

rm -rf "$STAGE" "$OUT"
mkdir -p "$STAGE"

cp "$DLL"                 "$STAGE/"
cp "$ROOT/README.md"      "$STAGE/"
cp "$ROOT/CHANGELOG.md"   "$STAGE/"
cp "$ROOT/manifest.json"  "$STAGE/"
cp "$ROOT/icon.png"       "$STAGE/"

# Guard against ever shipping a game binary.
if find "$STAGE" -iname 'assembly_*' -o -iname 'UnityEngine*' -o -iname 'BepInEx*' -o -iname '0Harmony*' | grep -q .; then
  echo "Refusing to package: a game or loader binary ended up in the staging folder." >&2
  exit 1
fi

# zip(1) is not present on every dev box (notably Git Bash on Windows), so fall back to
# Python's zipfile, then to PowerShell, before giving up.
if command -v zip >/dev/null 2>&1; then
  ( cd "$STAGE" && zip -q -r "$OUT" . )
elif command -v python >/dev/null 2>&1 || command -v python3 >/dev/null 2>&1; then
  PY="$(command -v python3 || command -v python)"
  "$PY" - "$STAGE" "$OUT" <<'PYEOF'
import os, sys, zipfile
stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as archive:
    for root, _, files in os.walk(stage):
        for name in sorted(files):
            full = os.path.join(root, name)
            archive.write(full, os.path.relpath(full, stage).replace(os.sep, "/"))
PYEOF
elif command -v powershell >/dev/null 2>&1; then
  powershell -NoProfile -Command \
    "Compress-Archive -Path '$STAGE/*' -DestinationPath '$OUT' -CompressionLevel Optimal"
else
  echo "No zip, python or powershell available to build the archive." >&2
  exit 1
fi

rm -rf "$STAGE"

echo "Package written to $OUT"
if command -v unzip >/dev/null 2>&1; then
  unzip -l "$OUT"
else
  ls -la "$OUT"
fi
