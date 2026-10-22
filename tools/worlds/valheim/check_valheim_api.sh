#!/bin/bash
# Checks that the Valheim functions and fields the plugin uses still exist in the installed game.
set -e
cd "$(dirname "$0")/../../.."
dotnet run --project tools/worlds/valheim/check_valheim_api -v q -- "$@"
