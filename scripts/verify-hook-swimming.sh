#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/swimming
dotnet build tools/HookIsland.Swimming.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Swimming.Tests/bin/Release/net8.0/HookIsland.Swimming.Tests.dll "$@" | tee artifacts/swimming/logic-network-tests.log
