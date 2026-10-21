#!/bin/bash
# Copies the ValOutcast bridge into third_party/OpenJK and applies the small patch to OpenJK's own files.
# Safe to run more than once. Then rebuild with: cmake --build third_party/OpenJK/buildjo -j
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OJK="$ROOT/third_party/OpenJK"
DEST="$OJK/codeJK2/game/valoutcast"
mkdir -p "$DEST"
cp "$ROOT/outcast/src/vo_bridge.h" "$ROOT/outcast/src/vo_bridge.cpp" "$ROOT/outcast/src/vo_field.h" "$ROOT/outcast/src/vo_field.cpp" "$DEST/"
cp "$ROOT/protocol/vo_link.h" "$ROOT/protocol/vo_link.c" "$DEST/"
cd "$OJK"
if git apply --check "$ROOT/outcast/patches/0001-bridge-hooks.patch" 2>/dev/null; then
	git apply "$ROOT/outcast/patches/0001-bridge-hooks.patch"
	echo "patch applied"
else
	echo "patch already applied (or does not apply cleanly)"
fi
