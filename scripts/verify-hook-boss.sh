#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/boss
dotnet build tools/HookIsland.Boss.Tests -c Release --nologo --disable-build-servers -m:1
dotnet tools/HookIsland.Boss.Tests/bin/Release/net8.0/HookIsland.Boss.Tests.dll "$@" | tee artifacts/boss/logic-network-tests.log
dotnet build tools/HookIsland.Boss.Adapter.Tests -c Release --nologo --disable-build-servers -m:1
dotnet tools/HookIsland.Boss.Adapter.Tests/bin/Release/net8.0/HookIsland.Boss.Adapter.Tests.dll | tee artifacts/boss/input-adapter-tests.log
