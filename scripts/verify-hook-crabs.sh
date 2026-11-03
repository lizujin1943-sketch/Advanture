#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/crabs
dotnet build tools/HookIsland.Crab.Tests -c Release --nologo --disable-build-servers -m:1
dotnet tools/HookIsland.Crab.Tests/bin/Release/net8.0/HookIsland.Crab.Tests.dll "$@" | tee artifacts/crabs/logic-network-tests.log
