#!/bin/bash
# Runs every test that needs no game running.
set -e
cd "$(dirname "$0")/.."
python3 tools/test_protocol.py
tools/test_c.sh | tail -1
tools/test_csharp.sh | tail -1
tools/test_field.sh
tools/worlds/valheim/check_valheim_api.sh
echo "ALL TESTS PASSED"
