#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/firearms
dotnet build tools/HookIsland.Firearm.Tests -c Release --nologo --disable-build-servers -m:1 -v:quiet
if ! dotnet tools/HookIsland.Firearm.Tests/bin/Release/net8.0/HookIsland.Firearm.Tests.dll "$@" > artifacts/firearms/results.log 2>&1; then
  cat artifacts/firearms/results.log
  exit 1
fi
cat artifacts/firearms/results.log
