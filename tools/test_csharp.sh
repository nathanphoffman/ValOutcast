#!/bin/bash
# Tests the C# link: golden packets, then connect to a fake player side and lose it.
set -e
cd "$(dirname "$0")/.."
dotnet build tools/csharp_test -nologo -v q -o "${TMPDIR:-/tmp}/vo_csharp_test" > /dev/null
timeout 3 python3 tools/fake_player.py > /dev/null &
dotnet "${TMPDIR:-/tmp}/vo_csharp_test/csharp_test.dll"
wait || true
