#!/bin/bash
# Builds and runs the collision field tests (pure math, no game needed).
set -e
cd "$(dirname "$0")/../outcast/src"
OUT=$(mktemp -d)
g++ -std=c++17 -Wall -Wextra -o "$OUT/test_vo_field" test_vo_field.cpp vo_field.cpp
"$OUT/test_vo_field"
rm -rf "$OUT"
