#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/character
dotnet build tools/HookIsland.Character.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Character.Tests/bin/Release/net8.0/HookIsland.Character.Tests.dll "$@" | tee artifacts/character/logic-network-tests.log
