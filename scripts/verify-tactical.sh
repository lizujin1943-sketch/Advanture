#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/tactical-network
dotnet build tools/TacticalGame.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/TacticalGame.Tests/bin/Release/net8.0/TacticalGame.Tests.dll | tee artifacts/tactical-network/tactical-tests.log
