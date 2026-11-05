#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/eldritch
dotnet build tools/HookIsland.Eldritch.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Eldritch.Tests/bin/Release/net8.0/HookIsland.Eldritch.Tests.dll | tee artifacts/eldritch/logic-network-tests.log
