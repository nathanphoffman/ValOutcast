#!/bin/bash
# Builds and tests the C link: golden packets, then connect to a fake world side and lose it.
set -e
cd "$(dirname "$0")/.."
OUT=$(mktemp -d)
gcc -Wall -Wextra -o "$OUT/test_vo_link" protocol/test_vo_link.c protocol/vo_link.c
"$OUT/test_vo_link"
# Fake world side runs for ~3 s, then exits so the C side sees it vanish.
timeout 3 python3 tools/fake_world.py > /dev/null &
"$OUT/test_vo_link" player
wait || true
rm -rf "$OUT"
