#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/melee
dotnet build tools/HookIsland.Melee.Tests -c Release --nologo
dotnet tools/HookIsland.Melee.Tests/bin/Release/net8.0/HookIsland.Melee.Tests.dll "$@" > artifacts/melee/results.log 2>&1 || { cat artifacts/melee/results.log; exit 1; }
cat artifacts/melee/results.log
