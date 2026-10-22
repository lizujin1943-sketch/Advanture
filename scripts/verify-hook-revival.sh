#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/revival
dotnet build tools/HookIsland.Revival.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Revival.Tests/bin/Release/net8.0/HookIsland.Revival.Tests.dll "$@" | tee artifacts/revival/logic-network-tests.log
