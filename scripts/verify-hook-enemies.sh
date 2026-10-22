#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/enemies
dotnet build tools/HookIsland.Enemy.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Enemy.Tests/bin/Release/net8.0/HookIsland.Enemy.Tests.dll "$@" | tee artifacts/enemies/logic-network-tests.log
