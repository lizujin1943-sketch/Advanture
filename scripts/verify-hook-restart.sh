#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/restart
dotnet build tools/HookIsland.Restart.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Restart.Tests/bin/Release/net8.0/HookIsland.Restart.Tests.dll "$@" | tee artifacts/restart/logic-network-tests.log
