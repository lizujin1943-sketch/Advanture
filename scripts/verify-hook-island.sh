#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/hook-island-playable
dotnet build tools/HookIsland.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Tests/bin/Release/net8.0/HookIsland.Tests.dll | tee artifacts/hook-island-playable/logic-network-tests.log
