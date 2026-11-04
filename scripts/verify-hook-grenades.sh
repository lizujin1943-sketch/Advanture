#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p artifacts/grenades
dotnet build tools/HookIsland.Grenade.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Grenade.Tests/bin/Release/net8.0/HookIsland.Grenade.Tests.dll | tee artifacts/grenades/verification.log
